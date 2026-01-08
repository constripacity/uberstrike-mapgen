"""ML Model Trainer.

Trains a classifier to predict blueprint quality (PASS/FAIL) based on 
extracted features (masks, scalars).
"""

from __future__ import annotations

import json
import joblib
import logging
import numpy as np
from pathlib import Path
from typing import List, Dict, Tuple
from tqdm import tqdm

from sklearn.ensemble import RandomForestClassifier
from sklearn.metrics import accuracy_score, classification_report
from sklearn.model_selection import train_test_split

logger = logging.getLogger(__name__)

class QualityModelTrainer:
    """Trains a QC predictor model."""

    def __init__(self, dataset_dir: Path):
        self.dataset_dir = dataset_dir
        self.samples_dir = dataset_dir / "samples"
        self.model = RandomForestClassifier(n_estimators=100, random_state=42)

    def load_data(self) -> Tuple[np.ndarray, np.ndarray, List[str]]:
        """Load features and labels from dataset."""
        X = []
        y = []
        feature_names = []
        
        # We look for all samples
        samples = list(self.samples_dir.iterdir())
        logger.info(f"Loading data from {len(samples)} samples...")
        
        valid_samples = 0
        for sample_dir in tqdm(samples, desc="Loading Data"):
            feat_path = sample_dir / "features.json"
            qc_path = sample_dir / "qc_report.json"
            
            if not feat_path.exists() or not qc_path.exists():
                continue
                
            try:
                with open(feat_path) as f:
                    feats = json.load(f)
                with open(qc_path) as f:
                    qc = json.load(f)
                
                # construct vector
                vector = [
                    feats.get("walkable_area_ratio", 0),
                    feats.get("wall_area_ratio", 0),
                    feats.get("spawn_count", 0),
                    feats.get("pickup_count", 0),
                    feats.get("loopiness_score", 0),
                    feats.get("chokepoint_score", 0)
                ]
                
                if not feature_names:
                    feature_names = [
                        "walkable_ratio", "wall_ratio", "spawn_count", 
                        "pickup_count", "loopiness", "chokepoint"
                    ]
                
                X.append(vector)
                
                # Label: Pass/Warn -> 1, Fail -> 0
                label = 1 if qc.get("status") in ["pass", "warn"] else 0
                y.append(label)
                valid_samples += 1
                
            except Exception:
                continue
                
        return np.array(X), np.array(y), feature_names

    def train(self, output_path: Path) -> Dict[str, float]:
        """Train and save model."""
        X, y, names = self.load_data()
        
        if len(X) == 0:
            raise ValueError("No valid data found to train on.")
            
        logger.info(f"Training on {len(X)} samples with {len(names)} features.")
        
        X_train, X_test, y_train, y_test = train_test_split(X, y, test_size=0.2, random_state=42)
        
        self.model.fit(X_train, y_train)
        
        # Evaluate
        train_pred = self.model.predict(X_train)
        test_pred = self.model.predict(X_test)
        
        train_acc = accuracy_score(y_train, train_pred)
        test_acc = accuracy_score(y_test, test_pred)
        
        logger.info(f"Train Acc: {train_acc:.3f}, Test Acc: {test_acc:.3f}")
        logger.info(f"\n{classification_report(y_test, test_pred)}")
        
        # Save
        artifact = {
            "model": self.model,
            "feature_names": names
        }
        joblib.dump(artifact, output_path)
        logger.info(f"Model saved to {output_path}")
        
        return {"train_acc": train_acc, "test_acc": test_acc}
