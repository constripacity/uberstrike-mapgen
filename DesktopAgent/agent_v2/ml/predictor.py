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
        prob = self.model.predict_proba(X)[0] # [prob_fail, prob_pass]
        pred_class = self.model.predict(X)[0]
        
        status = "PASS" if pred_class == 1 else "FAIL"
        confidence = prob[1] if pred_class == 1 else prob[0]
        
        return {
            "prediction": status,
            "confidence": float(confidence),
            "pass_probability": float(prob[1])
        }
