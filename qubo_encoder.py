"""
qubo_encoder.py — MapGen Constraint → QUBO Formulation

Converts UberStrike MapGen item placement constraints into a QUBO
(Quadratic Unconstrained Binary Optimization) matrix compatible with
both D-Wave Ocean SDK and Amazon Braket.

Variable encoding:
    x_{flat_item, candidate} ∈ {0,1}
    flat_item = sequential index across all item copies (0..N_items-1)
    candidate = candidate position index (0..N_candidates-1)
    
    Flattened variable index: var = flat_item * N_candidates + candidate
    x_{var} = 1 means "item `flat_item` is placed at candidate position `candidate`"

Constraints encoded (from MapGen analysis):
    C-001: Spawn Balance      (quadratic — pairwise advantage differences)
    C-002: Risk/Reward        (linear — precomputed exposure penalties)
    C-003: Flow Alignment     (linear — choke proximity penalties)
    C-004: Minimum Spacing    (quadratic — natural QUBO fit)
    C-005: Strategic Depth    (quadratic — power-item centroid offset)
    C-006: Walkability        (structural — candidates are walkable by construction)
    C-007: Item Counts        (quadratic — one-hot encoding)

Usage:
    encoder = MapGenQUBOEncoder(
        walkable_mask=mask,          # 2D numpy bool array
        spawn_points=[(x,z), ...],   # spawn coordinates
        choke_points=[(x,z), ...],   # choke point coordinates
        cover_positions=[(x,z), ...],# cover coordinates
        item_rules=UBERSTRIKE_ITEM_RULES
    )
    Q = encoder.encode()
    
    # Validate against known SA solution
    report = encoder.validate(Q, sa_placement_dict)
    
    # Decode quantum solution
    placement = encoder.decode(bitstring)

Author: Culo / Quantum MapGen Project
License: MIT
"""

import numpy as np
from typing import Dict, List, Tuple, Optional, Any
from collections import defaultdict
import json
import time


# ─────────────────────────────────────────────────────────
# Default item rules matching UberStrike MapGen SA config
# ─────────────────────────────────────────────────────────

UBERSTRIKE_ITEM_RULES = {
    "sniper": {
        "count": 1,
        "min_spacing": 50.0,
        "value": 10.0,
        "exposure_pref": "prefer_exposed",  # powerful → dangerous positions
        "is_power_item": True,
    },
    "rocket": {
        "count": 1,
        "min_spacing": 40.0,
        "value": 9.0,
        "exposure_pref": "prefer_exposed",
        "is_power_item": True,
    },
    "shotgun": {
        "count": 2,
        "min_spacing": 30.0,
        "value": 7.0,
        "exposure_pref": "prefer_exposed",
        "is_power_item": False,
    },
    "armor_heavy": {
        "count": 1,
        "min_spacing": 35.0,
        "value": 8.0,
        "exposure_pref": "prefer_cover",
        "is_power_item": True,
    },
    "armor_light": {
        "count": 3,
        "min_spacing": 20.0,
        "value": 4.0,
        "exposure_pref": "prefer_cover",
        "is_power_item": False,
    },
    "health_mega": {
        "count": 1,
        "min_spacing": 35.0,
        "value": 8.0,
        "exposure_pref": "prefer_exposed",
        "is_power_item": False,
    },
    "health_small": {
        "count": 6,
        "min_spacing": 15.0,
        "value": 2.0,
        "exposure_pref": "neutral",
        "is_power_item": False,
    },
    "ammo_rockets": {
        "count": 4,
        "min_spacing": 10.0,
        "value": 3.0,
        "exposure_pref": "neutral",
        "is_power_item": False,
    },
    "ammo_bullets": {
        "count": 6,
        "min_spacing": 8.0,
        "value": 1.0,
        "exposure_pref": "neutral",
        "is_power_item": False,
    },
}


class MapGenQUBOEncoder:
    """Converts MapGen placement constraints into QUBO format.
    
    The QUBO is returned as a dictionary {(i, j): weight} where i, j are
    flattened variable indices. Diagonal entries (i == i) are linear terms.
    Off-diagonal entries (i, j) where i < j are quadratic couplings.
    
    Compatible with:
        - D-Wave Ocean: dimod.BinaryQuadraticModel.from_qubo(Q)
        - Amazon Braket: via braket-ocean-plugin or manual QAOA circuit
    """

    def __init__(
        self,
        walkable_mask: np.ndarray,
        spawn_points: List[Tuple[float, float]],
        choke_points: List[Tuple[float, float]],
        cover_positions: List[Tuple[float, float]],
        item_rules: Dict[str, Dict] = None,
        candidate_stride: int = 4,
        meters_per_cell: float = 1.0,
    ):
        """
        Args:
            walkable_mask: 2D boolean array (True = walkable cell)
            spawn_points: List of (x, z) spawn coordinates in world units
            choke_points: List of (x, z) choke point coordinates
            cover_positions: List of (x, z) cover/wall-adjacent positions
            item_rules: Item placement rules dict (defaults to UBERSTRIKE_ITEM_RULES)
            candidate_stride: Sample every N cells from walkable mask
            meters_per_cell: Scale factor from grid cells to meters
        """
        self.rules = item_rules or UBERSTRIKE_ITEM_RULES
        self.meters_per_cell = meters_per_cell

        # Sample candidate positions from walkable mask
        self.candidates = self._sample_candidates(walkable_mask, candidate_stride)
        self.n_candidates = len(self.candidates)

        # Build flat item list: [(type_name, copy_index, rule_dict), ...]
        self.flat_items = []
        for item_type, rule in self.rules.items():
            for copy_idx in range(rule["count"]):
                self.flat_items.append((item_type, copy_idx, rule))
        self.n_items = len(self.flat_items)
        self.n_vars = self.n_items * self.n_candidates

        # Reference points
        self.spawns = np.array(spawn_points, dtype=np.float64) if spawn_points else np.empty((0, 2))
        self.chokes = np.array(choke_points, dtype=np.float64) if choke_points else np.empty((0, 2))
        self.covers = np.array(cover_positions, dtype=np.float64) if cover_positions else np.empty((0, 2))
        self.candidates_arr = np.array(self.candidates, dtype=np.float64)

        # Map center (for strategic depth)
        if walkable_mask.size > 0:
            self.map_center = np.array([walkable_mask.shape[1] / 2.0, walkable_mask.shape[0] / 2.0]) * meters_per_cell
        else:
            self.map_center = np.array([0.0, 0.0])

        # Precompute all distance matrices
        self._precompute_distances()

        # Build item group indices for fast lookup
        self._build_item_groups()

    # ─────────────────────────────────────────────────────
    # Initialization helpers
    # ─────────────────────────────────────────────────────

    def _sample_candidates(self, mask: np.ndarray, stride: int) -> List[Tuple[float, float]]:
        """Sample candidate positions from walkable cells."""
        candidates = []
        for y in range(0, mask.shape[0], stride):
            for x in range(0, mask.shape[1], stride):
                if mask[y, x]:
                    # Convert grid coords to world coords
                    wx = x * self.meters_per_cell
                    wz = y * self.meters_per_cell
                    candidates.append((wx, wz))
        if not candidates:
            raise ValueError("No walkable candidates found. Check walkable_mask and stride.")
        return candidates

    def _precompute_distances(self):
        """Precompute all distance matrices needed by constraints."""
        t0 = time.time()
        cands = self.candidates_arr  # (N, 2)

        # Candidate-to-candidate pairwise distances
        # Using broadcasting: (N,1,2) - (1,N,2) → (N,N,2) → norm → (N,N)
        diff = cands[:, np.newaxis, :] - cands[np.newaxis, :, :]
        self.dist_cc = np.sqrt(np.sum(diff ** 2, axis=2))

        # Candidate-to-spawn distances: (N_cands, N_spawns)
        if len(self.spawns) > 0:
            diff_s = cands[:, np.newaxis, :] - self.spawns[np.newaxis, :, :]
            self.dist_cs = np.sqrt(np.sum(diff_s ** 2, axis=2))
        else:
            self.dist_cs = np.empty((self.n_candidates, 0))

        # Candidate-to-nearest-choke distance: (N_cands,)
        if len(self.chokes) > 0:
            diff_ch = cands[:, np.newaxis, :] - self.chokes[np.newaxis, :, :]
            dist_all_chokes = np.sqrt(np.sum(diff_ch ** 2, axis=2))
            self.dist_choke_nearest = np.min(dist_all_chokes, axis=1)
        else:
            self.dist_choke_nearest = np.full(self.n_candidates, 10.0)  # neutral

        # Candidate-to-nearest-cover distance: (N_cands,)
        if len(self.covers) > 0:
            diff_co = cands[:, np.newaxis, :] - self.covers[np.newaxis, :, :]
            dist_all_covers = np.sqrt(np.sum(diff_co ** 2, axis=2))
            self.dist_cover_nearest = np.min(dist_all_covers, axis=1)
        else:
            self.dist_cover_nearest = np.full(self.n_candidates, 10.0)  # neutral

        # Candidate distance from map center: (N_cands,)
        self.dist_center = np.sqrt(np.sum((cands - self.map_center) ** 2, axis=1))

        self._precompute_time = time.time() - t0

    def _build_item_groups(self):
        """Build lookup structures for item types and power items."""
        self.type_to_flat_indices = defaultdict(list)
        self.power_item_indices = []
        for flat_idx, (item_type, copy_idx, rule) in enumerate(self.flat_items):
            self.type_to_flat_indices[item_type].append(flat_idx)
            if rule.get("is_power_item", False):
                self.power_item_indices.append(flat_idx)

    # ─────────────────────────────────────────────────────
    # Variable index helpers
    # ─────────────────────────────────────────────────────

    def var(self, item_flat: int, candidate: int) -> int:
        """Flattened variable index for item `item_flat` at candidate `candidate`."""
        return item_flat * self.n_candidates + candidate

    def unvar(self, v: int) -> Tuple[int, int]:
        """Reverse: flattened index → (item_flat, candidate)."""
        return divmod(v, self.n_candidates)

    # ─────────────────────────────────────────────────────
    # QUBO construction
    # ─────────────────────────────────────────────────────

    def encode(
        self,
        penalty_one_hot: float = 100.0,
        weight_spacing: float = 7.0,
        weight_spawn_balance: float = 10.0,
        weight_risk_reward: float = 5.0,
        weight_flow: float = 3.0,
        weight_depth: float = 4.0,
    ) -> Dict[Tuple[int, int], float]:
        """Build the full QUBO matrix.
        
        Args:
            penalty_one_hot: Penalty strength for one-hot constraint (C-007).
                Must dominate all other terms to enforce hard constraint.
            weight_*: Weights matching the SA energy function terms.
            
        Returns:
            Q: dict {(i, j): weight} where i <= j. 
               Diagonal (i,i) = linear terms. Off-diagonal = quadratic.
        """
        t0 = time.time()
        Q = defaultdict(float)

        # C-007: One-hot constraints (hard — each item placed exactly once)
        self._add_one_hot(Q, penalty_one_hot)

        # C-004: Minimum spacing (quadratic — natural QUBO fit)
        self._add_spacing(Q, weight_spacing)

        # C-001: Spawn balance (quadratic — pairwise advantage difference)
        if len(self.spawns) > 0:
            self._add_spawn_balance(Q, weight_spawn_balance)

        # C-002: Risk/Reward (linear — exposure penalties)
        self._add_risk_reward(Q, weight_risk_reward)

        # C-003: Flow alignment (linear — choke proximity)
        self._add_flow_alignment(Q, weight_flow)

        # C-005: Strategic depth (quadratic — power item centroid)
        if self.power_item_indices:
            self._add_strategic_depth(Q, weight_depth)

        # Convert to regular dict with canonical key ordering (i <= j)
        Q_clean = {}
        for (i, j), w in Q.items():
            if abs(w) < 1e-12:
                continue
            key = (min(i, j), max(i, j))
            Q_clean[key] = Q_clean.get(key, 0.0) + w

        self._encode_time = time.time() - t0
        return Q_clean

    def _add_one_hot(self, Q: dict, A: float):
        """C-007: Each item copy placed at exactly one candidate.
        
        For each item i: (Σ_j x_{i,j} - 1)² = 0
        Expands to: Σ_j x_{i,j} - 2·Σ_{j<k} x_{i,j}·x_{i,k} + 1
        
        QUBO terms:
            Linear:    +A on each x_{i,j}  (since x²=x for binary)
            Quadratic: -2A on each pair (x_{i,j}, x_{i,k}) within same item
            Constant:  +A per item (ignored, doesn't affect optimization)
        """
        for item_idx in range(self.n_items):
            for j in range(self.n_candidates):
                v_j = self.var(item_idx, j)
                # Linear term: +A (from x² coefficient in expansion)
                Q[(v_j, v_j)] += A

                # Quadratic terms: -2A for all pairs within this item
                for k in range(j + 1, self.n_candidates):
                    v_k = self.var(item_idx, k)
                    Q[(v_j, v_k)] += -2.0 * A

    def _add_spacing(self, Q: dict, weight: float):
        """C-004: Minimum spacing between items.
        
        For items (a, b) at candidates (j, l):
            If dist(j, l) < max(min_spacing_a, min_spacing_b):
                penalty = 5 * (required - actual)
                
        QUBO term: weight * penalty * x_{a,j} * x_{b,l}
        This is a natural quadratic — the core reason this problem fits QUBO.
        """
        for a in range(self.n_items):
            _, _, rule_a = self.flat_items[a]
            spacing_a = rule_a["min_spacing"]

            for b in range(a + 1, self.n_items):
                _, _, rule_b = self.flat_items[b]
                spacing_b = rule_b["min_spacing"]
                required = max(spacing_a, spacing_b)

                for j in range(self.n_candidates):
                    for l in range(self.n_candidates):
                        dist = self.dist_cc[j, l]
                        if dist < required:
                            penalty = 5.0 * (required - dist)
                            v_a = self.var(a, j)
                            v_b = self.var(b, l)
                            key = (min(v_a, v_b), max(v_a, v_b))
                            Q[key] += weight * penalty

    def _add_spawn_balance(self, Q: dict, weight: float):
        """C-001: Spawn balance — minimize variance in per-spawn advantage.
        
        For each spawn s, advantage_s = Σ_i value_i / (dist(placed_i, spawn_s) + 1)
        
        We minimize Σ_{s1<s2} (advantage_s1 - advantage_s2)²
        
        Each advantage_s is linear in placement variables:
            advantage_s = Σ_{item_i} Σ_{cand_j} [value_i / (dist(cand_j, spawn_s) + 1)] * x_{i,j}
        
        The squared difference of two linear expressions is quadratic.
        """
        n_spawns = len(self.spawns)
        if n_spawns < 2:
            return

        # Precompute advantage coefficients: coeff[item_flat][cand] per spawn
        # advantage_s = Σ coeff_s[item][cand] * x_{item,cand}
        # coeff_s[item][cand] = value / (dist(cand, spawn_s) + 1)
        coeffs = np.zeros((n_spawns, self.n_items, self.n_candidates))
        for item_idx, (_, _, rule) in enumerate(self.flat_items):
            value = rule["value"]
            for s in range(n_spawns):
                # dist_cs[cand, spawn] → (N_cands,)
                coeffs[s, item_idx, :] = value / (self.dist_cs[:, s] + 1.0)

        # For each spawn pair (s1, s2), add (advantage_s1 - advantage_s2)² terms
        # diff_coeff[item][cand] = coeffs[s1] - coeffs[s2]
        # The quadratic expansion: Σ_{a,j} Σ_{b,l} diff[a,j] * diff[b,l] * x_{a,j} * x_{b,l}
        scale = weight / (n_spawns * (n_spawns - 1) / 2)  # normalize by pair count

        for s1 in range(n_spawns):
            for s2 in range(s1 + 1, n_spawns):
                diff = coeffs[s1] - coeffs[s2]  # (n_items, n_candidates)

                # Linear terms (a==b, j==l): diff[a,j]² * x_{a,j}
                for a in range(self.n_items):
                    for j in range(self.n_candidates):
                        d = diff[a, j]
                        if abs(d) < 1e-8:
                            continue
                        v = self.var(a, j)
                        Q[(v, v)] += scale * d * d

                # Quadratic terms: 2 * diff[a,j] * diff[b,l] * x_{a,j} * x_{b,l}
                # Only for (a,j) < (b,l) to avoid double counting
                for a in range(self.n_items):
                    for j in range(self.n_candidates):
                        d_aj = diff[a, j]
                        if abs(d_aj) < 1e-8:
                            continue
                        v_a = self.var(a, j)

                        for b in range(a, self.n_items):
                            l_start = j + 1 if b == a else 0
                            for l in range(l_start, self.n_candidates):
                                d_bl = diff[b, l]
                                if abs(d_bl) < 1e-8:
                                    continue
                                v_b = self.var(b, l)
                                key = (min(v_a, v_b), max(v_a, v_b))
                                Q[key] += 2.0 * scale * d_aj * d_bl

    def _add_risk_reward(self, Q: dict, weight: float):
        """C-002: Risk/Reward — items should match their exposure preference.
        
        prefer_exposed items: penalty if nearest cover < 10m (too safe)
        prefer_cover items:   penalty if nearest cover > 15m (too exposed)
        neutral items:        no penalty
        
        All linear (diagonal) terms since penalty depends only on candidate position.
        """
        for item_idx, (_, _, rule) in enumerate(self.flat_items):
            pref = rule.get("exposure_pref", "neutral")
            if pref == "neutral":
                continue

            for j in range(self.n_candidates):
                cover_dist = self.dist_cover_nearest[j]
                penalty = 0.0

                if pref == "prefer_exposed" and cover_dist < 10.0:
                    penalty = 2.0 * (10.0 - cover_dist)
                elif pref == "prefer_cover" and cover_dist > 15.0:
                    penalty = cover_dist - 15.0

                if penalty > 0:
                    v = self.var(item_idx, j)
                    Q[(v, v)] += weight * penalty

    def _add_flow_alignment(self, Q: dict, weight: float):
        """C-003: Flow alignment — items shouldn't block chokes but stay in range.
        
        If nearest choke < 5m:  penalty = 3 * (5 - dist)   (blocking choke)
        If nearest choke > 15m: penalty = 0.5 * (dist - 15) (too far from action)
        
        All linear (diagonal) terms.
        """
        for item_idx in range(self.n_items):
            for j in range(self.n_candidates):
                choke_dist = self.dist_choke_nearest[j]
                penalty = 0.0

                if choke_dist < 5.0:
                    penalty = 3.0 * (5.0 - choke_dist)
                elif choke_dist > 15.0:
                    penalty = 0.5 * (choke_dist - 15.0)

                if penalty > 0:
                    v = self.var(item_idx, j)
                    Q[(v, v)] += weight * penalty

    def _add_strategic_depth(self, Q: dict, weight: float):
        """C-005: Strategic depth — power items shouldn't cluster far from center.
        
        Penalty on centroid of power items being >30m from map center.
        
        Centroid_x = (1/N_power) * Σ_i Σ_j pos_x[j] * x_{i,j}  (for power items)
        
        We penalize: max(0, |centroid - center| - 30)
        
        Approximated as quadratic: (centroid_x - center_x)² + (centroid_z - center_z)²
        with the threshold absorbed into a reduced weight for positions within 30m.
        """
        if not self.power_item_indices:
            return

        n_power = len(self.power_item_indices)
        cx, cz = self.map_center

        # For each power item at each candidate, compute the contribution
        # to centroid displacement squared
        # centroid_x = (1/n) * Σ pos_x[j] * x_{i,j}
        # (centroid_x - cx)² = (1/n²) * [Σ (pos_x[j]-cx) * x_{i,j}]²
        # This expands to quadratic terms between all power item placements

        for axis in range(2):  # x and z
            center_val = self.map_center[axis]

            # Deviation of each candidate from center along this axis
            if axis == 0:
                devs = self.candidates_arr[:, 0] - center_val
            else:
                devs = self.candidates_arr[:, 1] - center_val

            scale = weight / (n_power * n_power)

            # Linear terms: dev[j]² * x_{i,j}
            for pi in self.power_item_indices:
                for j in range(self.n_candidates):
                    d = devs[j]
                    v = self.var(pi, j)
                    Q[(v, v)] += scale * d * d

            # Quadratic terms: 2 * dev[j] * dev[l] * x_{a,j} * x_{b,l}
            for idx_a, pi_a in enumerate(self.power_item_indices):
                for j in range(self.n_candidates):
                    d_j = devs[j]
                    if abs(d_j) < 1e-8:
                        continue
                    v_a = self.var(pi_a, j)

                    for idx_b in range(idx_a, n_power):
                        pi_b = self.power_item_indices[idx_b]
                        l_start = j + 1 if idx_b == idx_a else 0
                        for l in range(l_start, self.n_candidates):
                            d_l = devs[l]
                            if abs(d_l) < 1e-8:
                                continue
                            v_b = self.var(pi_b, l)
                            key = (min(v_a, v_b), max(v_a, v_b))
                            Q[key] += 2.0 * scale * d_j * d_l

    # ─────────────────────────────────────────────────────
    # Solution decoding
    # ─────────────────────────────────────────────────────

    def decode(self, bitstring: List[int]) -> Dict[str, List[Tuple[float, float]]]:
        """Convert a QUBO solution bitstring to item→positions mapping.
        
        Args:
            bitstring: List of 0/1 values of length n_vars
            
        Returns:
            placement: dict {item_type: [(x,z), ...]}
            
        Raises:
            ValueError: If bitstring length doesn't match n_vars
        """
        if len(bitstring) != self.n_vars:
            raise ValueError(f"Expected bitstring of length {self.n_vars}, got {len(bitstring)}")

        placement = defaultdict(list)
        violations = []

        for item_idx, (item_type, copy_idx, rule) in enumerate(self.flat_items):
            selected = []
            for j in range(self.n_candidates):
                v = self.var(item_idx, j)
                if bitstring[v] == 1:
                    selected.append(j)

            if len(selected) == 0:
                violations.append(f"Item {item_type}[{copy_idx}]: no position selected")
            elif len(selected) > 1:
                violations.append(f"Item {item_type}[{copy_idx}]: {len(selected)} positions selected (one-hot violated)")
                # Take the first one as fallback
                placement[item_type].append(self.candidates[selected[0]])
            else:
                placement[item_type].append(self.candidates[selected[0]])

        return dict(placement), violations

    # ─────────────────────────────────────────────────────
    # Validation against classical SA
    # ─────────────────────────────────────────────────────

    def encode_solution(self, sa_placement: Dict[str, List[Tuple[float, float]]]) -> List[int]:
        """Convert a classical SA placement dict into a QUBO bitstring.
        
        For each placed item, finds the nearest candidate position and sets
        that variable to 1.
        
        Args:
            sa_placement: dict {item_type: [(x,z), ...]} from SA output
            
        Returns:
            bitstring: List of 0/1 of length n_vars
        """
        bitstring = [0] * self.n_vars
        item_offset = 0

        for item_type, rule in self.rules.items():
            positions = sa_placement.get(item_type, [])
            for copy_idx in range(rule["count"]):
                if copy_idx < len(positions):
                    pos = np.array(positions[copy_idx])
                    # Find nearest candidate
                    dists = np.sqrt(np.sum((self.candidates_arr - pos) ** 2, axis=1))
                    nearest_cand = int(np.argmin(dists))
                    v = self.var(item_offset + copy_idx, nearest_cand)
                    bitstring[v] = 1
            item_offset += rule["count"]

        return bitstring

    def compute_qubo_energy(self, Q: Dict[Tuple[int, int], float], bitstring: List[int]) -> float:
        """Compute QUBO energy for a given bitstring.
        
        E = Σ_{(i,j)} Q_{i,j} * x_i * x_j
        """
        energy = 0.0
        for (i, j), w in Q.items():
            energy += w * bitstring[i] * bitstring[j]
        return energy

    def compute_sa_energy(self, placement: Dict[str, List[Tuple[float, float]]]) -> Dict[str, float]:
        """Compute SA-style energy breakdown for a placement.
        
        Returns individual term scores matching the original SA implementation.
        """
        all_positions = []
        all_items = []
        for item_type, positions in placement.items():
            rule = self.rules.get(item_type, {})
            for pos in positions:
                all_positions.append(pos)
                all_items.append((item_type, rule))

        if not all_positions:
            return {"total": 0.0}

        pos_arr = np.array(all_positions)
        n = len(all_positions)

        # Spawn balance
        spawn_balance = 0.0
        if len(self.spawns) > 0:
            advantages = []
            for s in range(len(self.spawns)):
                adv = 0.0
                for idx, (itype, rule) in enumerate(all_items):
                    d = np.sqrt(np.sum((pos_arr[idx] - self.spawns[s]) ** 2))
                    adv += rule.get("value", 1.0) / (d + 1.0)
                advantages.append(adv)
            spawn_balance = float(np.std(advantages)) if len(advantages) > 1 else 0.0

        # Risk/Reward
        risk_reward = 0.0
        for idx, (itype, rule) in enumerate(all_items):
            pref = rule.get("exposure_pref", "neutral")
            if pref == "neutral":
                continue
            if len(self.covers) > 0:
                cover_d = float(np.min(np.sqrt(np.sum((self.covers - pos_arr[idx]) ** 2, axis=1))))
            else:
                cover_d = 10.0
            if pref == "prefer_exposed" and cover_d < 10.0:
                risk_reward += 2.0 * (10.0 - cover_d)
            elif pref == "prefer_cover" and cover_d > 15.0:
                risk_reward += cover_d - 15.0

        # Flow alignment
        flow_alignment = 0.0
        for idx in range(n):
            if len(self.chokes) > 0:
                choke_d = float(np.min(np.sqrt(np.sum((self.chokes - pos_arr[idx]) ** 2, axis=1))))
            else:
                choke_d = 10.0
            if choke_d < 5.0:
                flow_alignment += 3.0 * (5.0 - choke_d)
            elif choke_d > 15.0:
                flow_alignment += 0.5 * (choke_d - 15.0)

        # Spacing penalty
        spacing_penalty = 0.0
        for a in range(n):
            _, rule_a = all_items[a]
            sp_a = rule_a.get("min_spacing", 10.0)
            for b in range(a + 1, n):
                _, rule_b = all_items[b]
                sp_b = rule_b.get("min_spacing", 10.0)
                required = max(sp_a, sp_b)
                d = np.sqrt(np.sum((pos_arr[a] - pos_arr[b]) ** 2))
                if d < required:
                    spacing_penalty += 5.0 * (required - d)

        # Strategic depth
        strategic_depth = 0.0
        power_positions = []
        for idx, (itype, rule) in enumerate(all_items):
            if rule.get("is_power_item", False):
                power_positions.append(pos_arr[idx])
        if power_positions:
            centroid = np.mean(power_positions, axis=0)
            offset = np.sqrt(np.sum((centroid - self.map_center) ** 2))
            strategic_depth = max(0.0, offset - 30.0)

        return {
            "spawn_balance": spawn_balance,
            "risk_reward": risk_reward,
            "flow_alignment": flow_alignment,
            "spacing_penalty": spacing_penalty,
            "strategic_depth": strategic_depth,
            "weighted_total": (
                10.0 * spawn_balance
                + 5.0 * risk_reward
                + 3.0 * flow_alignment
                + 7.0 * spacing_penalty
                + 4.0 * strategic_depth
            ),
        }

    def validate(
        self, Q: Dict[Tuple[int, int], float], sa_placement: Dict[str, List[Tuple[float, float]]]
    ) -> Dict[str, Any]:
        """Validate QUBO formulation against a known SA solution.
        
        Encodes the SA solution as a bitstring, computes QUBO energy,
        and compares with SA energy to check formulation correctness.
        
        A valid formulation should show strong correlation between
        QUBO and SA energies (not exact match due to penalty terms
        and discretization, but monotonically related).
        """
        bitstring = self.encode_solution(sa_placement)
        qubo_energy = self.compute_qubo_energy(Q, bitstring)
        sa_breakdown = self.compute_sa_energy(sa_placement)

        # Check one-hot validity of the encoded bitstring
        one_hot_valid = True
        for item_idx in range(self.n_items):
            count = sum(bitstring[self.var(item_idx, j)] for j in range(self.n_candidates))
            if count != 1:
                one_hot_valid = False
                break

        # Check how many items mapped to candidates (discretization loss)
        items_mapped = sum(bitstring)

        return {
            "qubo_energy": qubo_energy,
            "sa_energy_breakdown": sa_breakdown,
            "sa_weighted_total": sa_breakdown["weighted_total"],
            "one_hot_valid": one_hot_valid,
            "items_mapped": items_mapped,
            "items_expected": self.n_items,
            "n_variables": self.n_vars,
            "n_candidates": self.n_candidates,
            "n_qubo_terms": len(Q),
            "precompute_time_s": self._precompute_time,
            "encode_time_s": self._encode_time,
            "status": "valid" if one_hot_valid and items_mapped == self.n_items else "issues_detected",
        }

    # ─────────────────────────────────────────────────────
    # Problem scaling utilities
    # ─────────────────────────────────────────────────────

    def create_subproblem(self, item_types: List[str], max_candidates: int = 20) -> "MapGenQUBOEncoder":
        """Create a smaller encoder with a subset of items and candidates.
        
        Useful for simulator experiments that need to stay within qubit limits.
        
        Args:
            item_types: List of item type names to include
            max_candidates: Maximum number of candidate positions
            
        Returns:
            New MapGenQUBOEncoder with reduced problem size
        """
        sub_rules = {k: v for k, v in self.rules.items() if k in item_types}

        # Subsample candidates (evenly spaced from existing candidates)
        if len(self.candidates) <= max_candidates:
            sub_candidates = self.candidates
        else:
            indices = np.linspace(0, len(self.candidates) - 1, max_candidates, dtype=int)
            sub_candidates = [self.candidates[i] for i in indices]

        # Create a minimal walkable mask from the candidate positions
        # (reconstruct a mask that marks candidate cells as walkable)
        if sub_candidates:
            max_x = max(c[0] for c in sub_candidates) / self.meters_per_cell + 1
            max_z = max(c[1] for c in sub_candidates) / self.meters_per_cell + 1
            mask = np.zeros((int(max_z) + 1, int(max_x) + 1), dtype=bool)
            for cx, cz in sub_candidates:
                gx = int(cx / self.meters_per_cell)
                gz = int(cz / self.meters_per_cell)
                if 0 <= gz < mask.shape[0] and 0 <= gx < mask.shape[1]:
                    mask[gz, gx] = True
        else:
            mask = np.ones((10, 10), dtype=bool)

        sub_encoder = MapGenQUBOEncoder(
            walkable_mask=mask,
            spawn_points=self.spawns.tolist() if len(self.spawns) > 0 else [],
            choke_points=self.chokes.tolist() if len(self.chokes) > 0 else [],
            cover_positions=self.covers.tolist() if len(self.covers) > 0 else [],
            item_rules=sub_rules,
            candidate_stride=1,  # use all cells since we pre-filtered
            meters_per_cell=self.meters_per_cell,
        )
        return sub_encoder

    # ─────────────────────────────────────────────────────
    # Export utilities
    # ─────────────────────────────────────────────────────

    def to_ocean_bqm(self, Q: Dict[Tuple[int, int], float]):
        """Convert QUBO to D-Wave Ocean BinaryQuadraticModel.
        
        Requires: pip install dimod
        """
        try:
            from dimod import BinaryQuadraticModel
        except ImportError:
            raise ImportError("Install dimod: pip install dimod")

        return BinaryQuadraticModel.from_qubo(Q)

    def get_stats(self) -> Dict[str, Any]:
        """Return problem size statistics."""
        return {
            "n_item_types": len(self.rules),
            "n_item_copies": self.n_items,
            "n_candidates": self.n_candidates,
            "n_variables": self.n_vars,
            "n_spawns": len(self.spawns),
            "n_chokes": len(self.chokes),
            "n_covers": len(self.covers),
            "power_items": len(self.power_item_indices),
            "item_breakdown": {
                itype: rule["count"] for itype, rule in self.rules.items()
            },
        }

    def to_json_metadata(self, Q: Dict[Tuple[int, int], float]) -> str:
        """Export problem metadata as JSON for documentation/reproducibility."""
        stats = self.get_stats()
        stats["n_qubo_terms"] = len(Q)
        stats["n_linear_terms"] = sum(1 for (i, j) in Q if i == j)
        stats["n_quadratic_terms"] = sum(1 for (i, j) in Q if i != j)
        stats["candidates"] = self.candidates
        stats["flat_items"] = [
            {"type": t, "copy": c, "spacing": r["min_spacing"], "value": r["value"]}
            for t, c, r in self.flat_items
        ]
        return json.dumps(stats, indent=2, default=str)


# ─────────────────────────────────────────────────────────
# Quick self-test
# ─────────────────────────────────────────────────────────

def _self_test():
    """Minimal smoke test with synthetic data."""
    print("=" * 60)
    print("MapGen QUBO Encoder — Self Test")
    print("=" * 60)

    # Create a simple 32×32 grid with center walkable
    mask = np.zeros((32, 32), dtype=bool)
    mask[4:28, 4:28] = True  # walkable interior

    spawns = [(8.0, 8.0), (24.0, 8.0), (8.0, 24.0), (24.0, 24.0)]
    chokes = [(16.0, 8.0), (8.0, 16.0), (24.0, 16.0), (16.0, 24.0)]
    covers = [(6.0, 6.0), (26.0, 6.0), (6.0, 26.0), (26.0, 26.0),
              (16.0, 16.0)]

    # Test with minimal problem: 3 power items only
    mini_rules = {
        "sniper": UBERSTRIKE_ITEM_RULES["sniper"],
        "rocket": UBERSTRIKE_ITEM_RULES["rocket"],
        "armor_heavy": UBERSTRIKE_ITEM_RULES["armor_heavy"],
    }

    print("\n[1] Creating encoder (3 items, stride=4)...")
    encoder = MapGenQUBOEncoder(
        walkable_mask=mask,
        spawn_points=spawns,
        choke_points=chokes,
        cover_positions=covers,
        item_rules=mini_rules,
        candidate_stride=4,
    )

    stats = encoder.get_stats()
    print(f"    Items: {stats['n_item_copies']}")
    print(f"    Candidates: {stats['n_candidates']}")
    print(f"    Variables: {stats['n_variables']}")

    print("\n[2] Encoding QUBO...")
    Q = encoder.encode()
    n_linear = sum(1 for (i, j) in Q if i == j)
    n_quad = sum(1 for (i, j) in Q if i != j)
    print(f"    Total terms: {len(Q)}")
    print(f"    Linear: {n_linear}, Quadratic: {n_quad}")
    print(f"    Precompute time: {encoder._precompute_time:.4f}s")
    print(f"    Encode time: {encoder._encode_time:.4f}s")

    # Create a fake SA solution to validate against
    print("\n[3] Validating against synthetic SA solution...")
    fake_sa = {
        "sniper": [(12.0, 12.0)],
        "rocket": [(20.0, 20.0)],
        "armor_heavy": [(12.0, 20.0)],
    }
    report = encoder.validate(Q, fake_sa)
    print(f"    Status: {report['status']}")
    print(f"    One-hot valid: {report['one_hot_valid']}")
    print(f"    Items mapped: {report['items_mapped']}/{report['items_expected']}")
    print(f"    QUBO energy: {report['qubo_energy']:.2f}")
    print(f"    SA weighted total: {report['sa_weighted_total']:.2f}")

    # Test subproblem creation
    print("\n[4] Creating subproblem (2 items, 10 candidates)...")
    sub = encoder.create_subproblem(["sniper", "rocket"], max_candidates=10)
    sub_stats = sub.get_stats()
    print(f"    Items: {sub_stats['n_item_copies']}")
    print(f"    Candidates: {sub_stats['n_candidates']}")
    print(f"    Variables: {sub_stats['n_variables']}")

    print("\n[5] Encoding subproblem QUBO...")
    Q_sub = sub.encode()
    print(f"    Terms: {len(Q_sub)}")

    print("\n" + "=" * 60)
    print("Self test complete.")
    print("=" * 60)


if __name__ == "__main__":
    _self_test()
