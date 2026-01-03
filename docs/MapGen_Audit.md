# MapGen Audit

**Unity editor version**

The project is currently set up for Unity `6000.2.6f2`, as recorded in `ProjectSettings/ProjectVersion.txt`.

**High-level Assets structure**

The Unity project under `UberStrikeGen/` stores authored content in `Assets/`. The top level contains a legacy `Code.cs` placeholder and the `_UberStrike/` tree. `_UberStrike/` further breaks down into `Blueprints/`, `Gameplay/`, `Maps/`, `Scripts/`, `Tests/`, and `Theme/`. Editor utilities today mostly live under `_UberStrike/Scripts/Editor/`, while gameplay/runtime code is mixed across `_UberStrike/Scripts/AI/`, `Optimization/`, and `Runtime/`.

**Existing scripts & menu hooks**

`_UberStrike/Scripts/AI/MapTrainingPipeline.cs` exposes `Tools/UberStrike/Export Training Data` for writing ML datasets out of blueprint stacks. `StackLoader.cs` is duplicated under both `_UberStrike/Scripts/AI/` and `_UberStrike/Scripts/Editor/`, providing helpers to hydrate stack JSON + layer textures. `StackDefinition.cs` sits beside MapTrainingPipeline and houses the serializable data model consumed by stack tooling.

**Known issues**

Editor-only classes such as `MapTrainingPipeline` and `StackLoader` live outside an `Editor/` assembly and only partially guard their code with `#if UNITY_EDITOR`. Two copies of `StackLoader` exist (AI vs. Editor folders) with the same namespace/type name, leading to duplicate type definition errors once assembly definition files are introduced. These assets currently compile inside the monolithic default assembly but will fail once we isolate runtime/editor assemblies.

