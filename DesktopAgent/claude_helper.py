"""
Claude API Build Helper
Use Claude to generate files, review code, and debug
"""

import anthropic
import json
from pathlib import Path

# IMPORTANT: Replace with your actual API key from https://console.anthropic.com/
API_KEY = "YOUR_API_KEY_HERE"

client = anthropic.Anthropic(api_key=API_KEY)

class ClaudeBuildAssistant:
    def __init__(self):
        self.project_dir = Path("C:/DesktopAgent")
        self.conversation = []
    
    def ask(self, question):
        """Ask Claude a question about your project"""
        self.conversation.append({"role": "user", "content": question})
        
        response = client.messages.create(
            model="claude-sonnet-4-5-20250929",
            max_tokens=4096,
            messages=self.conversation
        )
        
        answer = response.content[0].text
        self.conversation.append({"role": "assistant", "content": answer})
        
        return answer
    
    def generate_file(self, file_path, description):
        """Ask Claude to generate a specific file"""
        prompt = f"""Generate the complete code for: {file_path}

Description: {description}

Context: This is for a Desktop Agent automation system with:
- FastAPI backend
- LLM integration (Ollama/local models)
- Screen capture and UI automation tools
- Tool calling system

Please provide production-ready code with error handling."""
        
        return self.ask(prompt)
    
    def review_code(self, file_path):
        """Have Claude review a file"""
        full_path = self.project_dir / file_path
        if not full_path.exists():
            return f"File not found: {file_path}"
        
        with open(full_path, 'r') as f:
            code = f.read()
        
        prompt = f"""Review this code from {file_path}:
```python
{code}
```

Please identify:
1. Bugs or errors
2. Security issues  
3. Performance improvements
4. Best practices"""
        
        return self.ask(prompt)
    
    def fix_error(self, error_message, file_path=None):
        """Debug an error with Claude's help"""
        prompt = f"I'm getting this error: {error_message}"
        if file_path:
            prompt += f"\n\nIn file: {file_path}"
        prompt += "\n\nHow do I fix it?"
        
        return self.ask(prompt)

# Quick usage examples
if __name__ == "__main__":
    if API_KEY == "YOUR_API_KEY_HERE":
        print("??  Please set your API key first!")
        print("Get it from: https://console.anthropic.com/")
        print("\nEdit this file and replace YOUR_API_KEY_HERE")
        exit(1)
    
    assistant = ClaudeBuildAssistant()
    
    print("=== Claude API Assistant ===\n")
    print("Examples:")
    print("1. assistant.ask('How do I add a new tool to the system?')")
    print("2. assistant.review_code('agent/main.py')")
    print("3. assistant.fix_error('NoneType object has no attribute lower')")
    print("\nInteractive mode:")
    
    while True:
        q = input("\nAsk Claude (or 'quit'): ")
        if q.lower() == 'quit':
            break
        
        answer = assistant.ask(q)
        print(f"\n{answer}\n")

