"""
ask_claude.py - Desktop Agent CLI
Sends questions to Claude with access to all registered Desktop Agent tools
"""
import anthropic
import sys
import os
from datetime import datetime
from agent.tool_registry import TOOL_REGISTRY
import asyncio
import json
from dotenv import load_dotenv

# Load environment variables from .env file
load_dotenv()

# Initialize Anthropic client
client = anthropic.Anthropic(api_key=os.environ.get("YOUR_API_KEY_HERE"))

def read_question(filename):
    """Read question from file"""
    try:
        with open(filename, 'r', encoding='utf-8') as f:
            content = f.read().strip()
            if not content:
                print(f"Error: {filename} is empty")
                sys.exit(1)
            return content
    except FileNotFoundError:
        print(f"Error: File '{filename}' not found")
        sys.exit(1)
    except Exception as e:
        print(f"Error reading file: {e}")
        sys.exit(1)

def build_tool_definitions():
    """Build tool definitions from registry for Claude API"""
    tools = []
    
    for tool_name, tool_func in TOOL_REGISTRY.items():
        # Create a basic tool definition
        # You can enhance this with proper parameter schemas later
        tools.append({
            "name": tool_name.replace(".", "_"),  # API doesn't like dots
            "description": f"Execute {tool_name} tool",
            "input_schema": {
                "type": "object",
                "properties": {},
                "required": []
            }
        })
    
    return tools

async def execute_tool(tool_name, tool_input):
    """Execute a tool from the registry"""
    # Convert API tool name back to registry name
    registry_name = tool_name.replace("_", ".", 1)  # Replace first underscore with dot
    
    if registry_name not in TOOL_REGISTRY:
        return {"error": f"Tool {registry_name} not found in registry"}
    
    tool_func = TOOL_REGISTRY[registry_name]
    
    try:
        # Check if tool is async
        if asyncio.iscoroutinefunction(tool_func):
            result = await tool_func(**tool_input)
        else:
            result = tool_func(**tool_input)
        
        return result
    except Exception as e:
        return {"error": f"Tool execution failed: {str(e)}"}

async def chat_with_tools(question):
    """Send question to Claude with tool support"""
    print("Asking Claude...")
    
    messages = [{"role": "user", "content": question}]
    tools = build_tool_definitions()
    
    # If no tools available, use simple mode
    if not tools:
        print("Warning: No tools registered")
        response = client.messages.create(
            model="claude-sonnet-4-5-20250929",
            max_tokens=8000,
            messages=messages
        )
        return response.content[0].text
    
    # Tool-enabled conversation loop
    max_iterations = 25  # Allow more tool calls for long builds
    iteration = 0
    
    while iteration < max_iterations:
        iteration += 1
        
        response = client.messages.create(
            model="claude-sonnet-4-5-20250929",
            max_tokens=8000,
            messages=messages,
            tools=tools
        )
        
        # Check if Claude wants to use tools
        if response.stop_reason == "tool_use":
            # Add assistant's response to messages
            messages.append({
                "role": "assistant",
                "content": response.content
            })
            
            # Execute requested tools
            tool_results = []
            for content_block in response.content:
                if content_block.type == "tool_use":
                    tool_name = content_block.name
                    tool_input = content_block.input
                    
                    print(f"  → Using tool: {tool_name}")
                    
                    # Execute the tool
                    result = await execute_tool(tool_name, tool_input)
                    
                    tool_results.append({
                        "type": "tool_result",
                        "tool_use_id": content_block.id,
                        "content": json.dumps(result)
                    })
            
            # Add tool results to messages
            messages.append({
                "role": "user",
                "content": tool_results
            })
            
        else:
            # Claude is done, return final response
            final_text = ""
            for content_block in response.content:
                if hasattr(content_block, "text"):
                    final_text += content_block.text
            
            return final_text
    
    return "Max iterations reached"

def main():
    if len(sys.argv) < 2:
        print("Usage: python ask_claude.py <question_file.txt>")
        sys.exit(1)
    
    question_file = sys.argv[1]
    question = read_question(question_file)
    
    # Run async chat
    answer = asyncio.run(chat_with_tools(question))
    
    print(answer)
    
    # Save answer
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    answer_file = f"answer_{timestamp}.txt"
    with open(answer_file, 'w', encoding='utf-8') as f:
        f.write(answer)
    print(f"\n✓ Answer saved to {answer_file}")

if __name__ == "__main__":
    main()
