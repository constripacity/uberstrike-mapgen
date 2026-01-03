"""DesktopAgent v2.0 quick usage examples."""

from __future__ import annotations

import asyncio

from agent_v2.generator.layer_generator import AILayerGenerator
from agent_v2.monitor.unity_monitor import UnityMapMonitor
from agent_v2.validator.stack_validator import StackValidator
from agent_v2.cli.assistant_cli import InteractiveCLI

# Example 1: Generate map from description
gen = AILayerGenerator()
stack_path = gen.generate_from_prompt(
    "Create a symmetric 4-room arena with central courtyard, elevated bridges, red and blue team spawns"
)
print(f"Generated stack at: {stack_path}")

# Example 2: Monitor and auto-fix
monitor = UnityMapMonitor()
asyncio.run(monitor.monitor_generation())

# Example 3: Validate existing stack
validator = StackValidator()
result = validator.validate_stack("ArenaStack_Sample.stack.json")
print(f"Validation: {result['status']}")

# Example 4: Interactive mode
cli = InteractiveCLI()
cli.run()  # Launches dashboard
