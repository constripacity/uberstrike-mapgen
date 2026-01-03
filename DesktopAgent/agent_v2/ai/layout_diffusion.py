"""Diffusion-based layout generation utilities.

The implementation intentionally keeps the training loop lightweight so it can
run without a heavy dependency stack. When PyTorch/diffusers are available we
wire them in, otherwise we fall back to statistical sampling based on the
exported dataset from the Unity editor.
"""
from __future__ import annotations

import json
import math
import random
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, List, Optional

import numpy as np
from PIL import Image

try:  # Optional heavy dependencies
    import torch
    from diffusers import DDPMScheduler, UNet2DModel  # type: ignore
except Exception:  # pragma: no cover - torch is optional
    torch = None  # type: ignore
    DDPMScheduler = None  # type: ignore
    UNet2DModel = None  # type: ignore

DATASET_DEFAULT = Path("Assets/_UberStrike/TrainingData/map_dataset.json")
MODEL_DIR = Path("DesktopAgent/models")
MODEL_DIR.mkdir(parents=True, exist_ok=True)


@dataclass
class LayoutSample:
    name: str
    layout: np.ndarray  # shape (H, W)
    wall_coverage: float
    spawn_balance: float


class LayoutDiffusionModel:
    """Produces layout masks using DDPM when possible.

    The class gracefully degrades to a statistical noise sampler when the
    PyTorch stack is not present, allowing CI to execute without GPUs.
    """

    def __init__(self, image_size: int = 256, model_path: Path | None = None) -> None:
        self.image_size = image_size
        self.model_path = model_path or MODEL_DIR / "layout_diffusion.pt"
        self.device = "cuda" if torch and torch.cuda.is_available() else "cpu"
        self._model: Optional[UNet2DModel] = None
        self._scheduler: Optional[DDPMScheduler] = None

    # ------------------------------------------------------------------
    # Training
    # ------------------------------------------------------------------
    def train_on_maps(self, dataset_path: Path | str = DATASET_DEFAULT, epochs: int = 100) -> None:
        """Train a diffusion model on the exported dataset.

        The method streams layout samples from the dataset JSON. When PyTorch is
        unavailable we record dataset statistics so the fallback sampler can
        mimic the learned distribution.
        """

        samples = list(self._load_dataset(dataset_path))
        if not samples:
            raise RuntimeError("No training samples found – export data from Unity first")

        if torch is None or UNet2DModel is None or DDPMScheduler is None:
            # Cache aggregate statistics for the heuristic generator
            stats = {
                "wall_mean": float(np.mean([s.wall_coverage for s in samples])),
                "wall_std": float(np.std([s.wall_coverage for s in samples])),
                "spawn_mean": float(np.mean([s.spawn_balance for s in samples])),
            }
            np.save(self.model_path.with_suffix(".stats.npy"), stats)
            return

        dataset = torch.tensor(np.stack([self._resize_layout(s.layout) for s in samples]), dtype=torch.float32)
        dataset = dataset.unsqueeze(1)  # channels

        model = UNet2DModel(sample_size=self.image_size, in_channels=1, out_channels=1, layers_per_block=2, block_out_channels=(64, 128, 256))
        scheduler = DDPMScheduler(num_train_timesteps=1000)
        optim = torch.optim.Adam(model.parameters(), lr=1e-4)

        model.to(self.device)
        dataset = dataset.to(self.device)

        for epoch in range(epochs):
            noise = torch.randn_like(dataset)
            timesteps = torch.randint(0, scheduler.config.num_train_timesteps, (dataset.shape[0],), device=self.device, dtype=torch.long)
            noisy = scheduler.add_noise(dataset, noise, timesteps)
            pred = model(noisy, timesteps).sample
            loss = torch.nn.functional.mse_loss(pred, noise)

            optim.zero_grad()
            loss.backward()
            optim.step()

            if epoch % 20 == 0:
                print(f"[LayoutDiffusion] Epoch {epoch} loss {loss.item():.4f}")

        torch.save({"model": model.state_dict(), "config": model.config}, self.model_path)
        self._model = model
        self._scheduler = scheduler

    # ------------------------------------------------------------------
    # Generation
    # ------------------------------------------------------------------
    def generate_layout(self, prompt_embedding: Optional[np.ndarray] = None) -> Image.Image:
        """Generate a new layout mask.

        The prompt embedding is currently used as a random seed vector. Future
        iterations can map natural language embeddings to latent offsets.
        """

        if torch is None or not self.model_path.exists():
            return self._generate_statistical(prompt_embedding)

        self._ensure_model_loaded()
        assert self._model is not None
        assert self._scheduler is not None

        batch_size = 1
        if prompt_embedding is not None:
            seed = int(abs(hash(tuple(np.asarray(prompt_embedding).flatten()))))
            generator = torch.Generator(device=self.device).manual_seed(seed)
        else:
            generator = torch.Generator(device=self.device)

        sample = torch.randn((batch_size, 1, self.image_size, self.image_size), generator=generator, device=self.device)
        self._scheduler.set_timesteps(1000)
        for t in self._scheduler.timesteps:
            with torch.no_grad():
                noise_pred = self._model(sample, t).sample
            sample = self._scheduler.step(noise_pred, t, sample).prev_sample

        layout = sample.clamp(-1, 1).cpu().numpy()[0, 0]
        layout = (layout - layout.min()) / (layout.max() - layout.min() + 1e-6)
        img = (layout > 0.5).astype(np.uint8) * 255
        return Image.fromarray(img, mode="L")

    def interpolate_maps(self, map_a: Path | str, map_b: Path | str, steps: int = 10) -> List[Image.Image]:
        """Blend two layouts in latent space.

        When the diffusion model is unavailable the method returns simple
        cross-fades between the raw images.
        """

        layout_a = self._load_image(map_a)
        layout_b = self._load_image(map_b)

        if torch is None or not self.model_path.exists():
            return self._lerp_images(layout_a, layout_b, steps)

        self._ensure_model_loaded()
        assert self._model is not None

        tensor_a = torch.tensor(self._resize_layout(layout_a), dtype=torch.float32, device=self.device).unsqueeze(0).unsqueeze(0)
        tensor_b = torch.tensor(self._resize_layout(layout_b), dtype=torch.float32, device=self.device).unsqueeze(0).unsqueeze(0)

        latents_a = self._model.conv_in(tensor_a)
        latents_b = self._model.conv_in(tensor_b)

        images: List[Image.Image] = []
        for i in range(steps):
            alpha = i / max(steps - 1, 1)
            latent = latents_a * (1 - alpha) + latents_b * alpha
            decoded = self._model.conv_out(latent)
            arr = decoded.squeeze().detach().cpu().numpy()
            arr = (arr - arr.min()) / (arr.max() - arr.min() + 1e-6)
            images.append(Image.fromarray((arr > 0.5).astype(np.uint8) * 255, mode="L"))

        return images

    # ------------------------------------------------------------------
    # Helpers
    # ------------------------------------------------------------------
    def _ensure_model_loaded(self) -> None:
        if self._model is not None and self._scheduler is not None:
            return
        if torch is None:
            raise RuntimeError("PyTorch is required for diffusion generation")
        if not self.model_path.exists():
            raise FileNotFoundError(f"No trained model at {self.model_path}")

        checkpoint = torch.load(self.model_path, map_location=self.device)
        config = checkpoint.get("config")
        self._model = UNet2DModel(**config)
        self._model.load_state_dict(checkpoint["model"])
        self._model.to(self.device)
        self._scheduler = DDPMScheduler(num_train_timesteps=1000)

    def _load_dataset(self, dataset_path: Path | str) -> Iterable[LayoutSample]:
        path = Path(dataset_path)
        if not path.exists():
            return []

        data = json.loads(path.read_text())
        for entry in data.get("maps", []):
            layout = np.array(entry.get("layoutFeatures", []), dtype=np.float32)
            size = int(math.sqrt(len(layout))) or self.image_size
            layout = layout.reshape((size, size))
            yield LayoutSample(
                name=entry.get("name", "unknown"),
                layout=layout,
                wall_coverage=float(entry.get("wallCoverage", 0.0)),
                spawn_balance=float(entry.get("spawnBalance", 0.0)),
            )

    def _resize_layout(self, layout: np.ndarray) -> np.ndarray:
        img = Image.fromarray(layout.astype(np.float32))
        img = img.resize((self.image_size, self.image_size), resample=Image.NEAREST)
        return np.array(img, dtype=np.float32)

    def _generate_statistical(self, prompt_embedding: Optional[np.ndarray]) -> Image.Image:
        stats_path = self.model_path.with_suffix(".stats.npy")
        if stats_path.exists():
            stats = np.load(stats_path, allow_pickle=True).item()
            wall_mean = stats.get("wall_mean", 0.35)
            wall_std = stats.get("wall_std", 0.1)
        else:
            wall_mean = 0.35
            wall_std = 0.1

        rng = np.random.default_rng()
        if prompt_embedding is not None:
            seed = int(abs(hash(tuple(np.asarray(prompt_embedding).flatten()))))
            rng = np.random.default_rng(seed)

        base = rng.random((self.image_size, self.image_size))
        threshold = np.clip(rng.normal(wall_mean, wall_std), 0.1, 0.8)
        layout = (base > threshold).astype(np.uint8) * 255
        return Image.fromarray(layout, mode="L")

    def _load_image(self, path: Path | str) -> np.ndarray:
        img = Image.open(path).convert("L")
        img = img.resize((self.image_size, self.image_size), Image.NEAREST)
        return (np.array(img) > 127).astype(np.float32)

    def _lerp_images(self, a: np.ndarray, b: np.ndarray, steps: int) -> List[Image.Image]:
        images: List[Image.Image] = []
        for i in range(steps):
            alpha = i / max(steps - 1, 1)
            arr = a * (1 - alpha) + b * alpha
            images.append(Image.fromarray((arr > 0.5).astype(np.uint8) * 255, mode="L"))
        return images


def generate_prompt_seed(prompt: str) -> np.ndarray:
    """Create a deterministic embedding vector from a natural language prompt."""

    rng = random.Random(hash(prompt))
    return np.array([rng.random() for _ in range(32)], dtype=np.float32)
