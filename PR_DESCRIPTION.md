# Summary
- add deterministic seeding utilities and propagate seeds through WFC, Voronoi, annealing, batch/orchestrator flows with explicit failure handling
- de-duplicate Unity types (MapTheme/GamePlaySet/DestructibleMarker/ProgressiveCombiner), restructure editor/runtime separation, and add asmdefs plus VERIFY instructions
- introduce pytest coverage for deterministic algorithms and add CI workflows (Python lint/test, Unity placeholder) with testing docs/badges

# Testing
- pytest DesktopAgent/tests
