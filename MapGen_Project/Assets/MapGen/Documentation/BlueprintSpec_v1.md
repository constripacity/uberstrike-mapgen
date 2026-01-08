# MapGen Blueprint Specification v1

This document defines the contract for a valid MapGen Blueprint. Any input that does not adhere to this spec will be rejected by the generation pipeline.

## 1. File Structure

A Map Blueprint consists of a set of image files (layers) and a JSON definition file (`.stack.json`).

### 1.1. The Manifest (`.stack.json`)
The entry point is a JSON file defining the map properties and references to layer images.

**Required Fields:**
*   `sourceName`: String (e.g., "Arena_Prime")
*   `directory`: String (Relative path context for images)
*   `metersPerPixel`: Float (Default: 0.2, must be > 0.01)
*   `wallHeight`: Float (Default: 4.0)

**Layer References (Paths relative to `directory`):**
*   `layoutPath` (Required): The geometric backbone (Floors, Walls).
*   `heightPath` (Optional): Greyscale heightmap (0=FloorLevel, 255=MaxHeight).
*   `flowPath` (Optional): Gameplay objects (Spawns, Items, Teleporters).
*   `themePath` (Optional): Biome/Texturing mask.
*   `lightingPath` (Optional): Light placement/baking hints.
*   `collisionPath` (Optional): Specialized collision overrides.

### 1.2. Layer Images (PNG)
*   **Format**: PNG (Lossless).
*   **Dimensions**: All layers MUST match the dimensions of `layoutPath`.
*   **Color Space**: sRGB (Textures should be read as uncompressed, point filtered).

## 2. Color Legend (Strict)

The pipeline uses strict color matching. Colors must match exactly (tolerance < 10/255).

### 2.1. Layout Layer (Geometry)
| Element | Hex Code | RGB | Notes |
| :--- | :--- | :--- | :--- |
| **Empty / Void** | `#000000` | (0, 0, 0) | No geometry. |
| **Floor** | `#B8B8B8` | (184, 184, 184) | Walkable ground. |
| **Wall** | `#444444` | (68, 68, 68) | Solid wall (height defined by `wallHeight`). |
| **Glass / Bridge** | `#00FFFF` | (0, 255, 255) | Transparent/Translucent walkable surface. |
| **Water** | `#0044FF` | (0, 68, 255) | Liquid hazard/volume. |

### 2.2. Flow Layer (Gameplay)
| Element | Hex Code | RGB | Notes |
| :--- | :--- | :--- | :--- |
| **Spawn Point** | `#FFFF00` | (255, 255, 0) | Player spawn location. |
| **Jump Pad** | `#00FF00` | (0, 255, 0) | Vertical traversal. |
| **Teleporter** | `#FF00FF` | (255, 0, 255) | Instant travel (paired). |
| **Health Pickup** | `#FF0000` | (255, 0, 0) | |
| **Armor Pickup** | `#FF7F00` | (255, 127, 0) | |
| **Ammo Pickup** | `#00AEEF` | (0, 174, 239) | |

### 2.3. Theme Layer
*   Uses a palette of colors mapped to `ThemeDefinition` ScriptableObjects (to be defined in Mission 5).

## 3. Validation Rules (CI/QC)

A blueprint is **INVALID** if:
1.  `layoutPath` does not exist or is not a valid PNG.
2.  Any optional layer provided does not match the dimensions of `layoutPath`.
3.  `metersPerPixel` is <= 0.
4.  **Connectivity check**: The map has > 1 disjoint walkable component (islands are unreachable).
5.  **Spawn check**: Fewer than 8 valid spawn points found.
6.  **Loop check**: Estimated loops < 2 (linear maps are bad for arena FPS).

## 4. Stack JSON Schema
(See accompanying `stack_schema.json`)
