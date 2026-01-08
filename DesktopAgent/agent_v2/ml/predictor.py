"""ML Predictor for inference."""

from __future__ import annotations

import joblib
import logging
import numpy as np
from pathlib import Path
from typing import Dict, Any

logger = logging.getLogger(__name__)

class QualityPredictor:
    """Predicts quality of a map using trained model."""

    def __init__(self, model_path: Path):
        if not model_path.exists():
            raise FileNotFoundError(f"Model not found at {model_path}")
            
        artifact = joblib.load(model_path)
        self.model = artifact["model"]
        self.feature_names = artifact["feature_names"]

    def predict(self, features: Dict[str, float]) -> Dict[str, Any]:
        """Predict pass/fail probability."""
        
        # Vectorize
        # Strictly order by feature_names
        vector = []
        mapping = {
            "walkable_ratio": "walkable_area_ratio",
            "wall_ratio": "wall_area_ratio",
            "spawn_count": "spawn_count",
            "pickup_count": "pickup_count",
            "loopiness": "loopiness_score",
            "chokepoint": "chokepoint_score"
        }
        
        for name in self.feature_names:
            key = mapping.get(name, name)
            vector.append(features.get(key, 0.0))
            
        X = np.array([vector])
        
        # Predict
        # Handle edge case where model only knows one class (e.g. all Pass)
        if len(self.model.classes_) == 1:
             class_label = self.model.classes_[0]
             # If strictly one class, prob is 1.0 for that class
             pred_class = class_label
             prob = [1.0] # Dummy
             # If class is 1 (Pass), confidence is 1.0. If 0 (Fail), confidence 1.0 that it fails.
             confidence = 1.0
             pass_prob = 1.0 if class_label == 1 else 0.0
        else:
             probs = self.model.predict_proba(X)[0] # [prob_fail, prob_pass] usually
             pred_class = self.model.predict(X)[0]
             
             # Map index to class? RandomForest classes_ are sorted [0, 1] usually
             # If [0, 1]:
             if list(self.model.classes_) == [0, 1]:
                 pass_prob = probs[1]
                 confidence = probs[1] if pred_class == 1 else probs[0]
             else:
                 # Fallback
                 pass_prob = 0.0
                 confidence = np.max(probs)

        status = "PASS" if pred_class == 1 else "FAIL"
        
        return {
            "prediction": status,
            "confidence": float(confidence),
            "pass_probability": float(pass_prob)
        }
