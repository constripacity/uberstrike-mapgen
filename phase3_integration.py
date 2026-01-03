"""Entry-point script for orchestrating phase 3 workflows."""
from __future__ import annotations

import asyncio
from pathlib import Path

from DesktopAgent.agent_v2.ai.layout_diffusion import LayoutDiffusionModel
from DesktopAgent.agent_v2.generator.variation_generator import MapVariationGenerator
from DesktopAgent.web_editor import app as web_app


async def main() -> None:
    print("🚀 Phase 3 integration booting up")

    dataset = Path("Assets/_UberStrike/TrainingData/map_dataset.json")
    model = LayoutDiffusionModel()
    if dataset.exists() and not model.model_path.exists():
        print("📚 Training layout diffusion model from dataset")
        model.train_on_maps(dataset)
    else:
        print("ℹ️ Dataset missing or model already present – skipping training")

    generator = MapVariationGenerator()
    print(f"🎲 Variation generator ready (output dir: {generator.output_dir})")

    print("🌐 Launching collaborative editor at http://localhost:5000")
    web_app.socketio.run(web_app.app, host="0.0.0.0", port=5000)


if __name__ == "__main__":
    asyncio.run(main())
