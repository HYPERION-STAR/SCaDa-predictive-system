import json
import re

with open('SCaDa_live_snapshot.json', 'r', encoding='utf-8') as f:
    content = f.read()

# Find all single quotes
matches = [m.start() for m in re.finditer("'", content)]
print(f'Total single quotes found: {len(matches)}')
for pos in matches[:20]:
    context = content[max(0,pos-30):pos+30]
    print(f'Position {pos}: ...{context}...')

# Try to find the exact error position
try:
    json.loads(content)
except json.JSONDecodeError as e:
    print(f'\nJSON Error at line {e.lineno}, col {e.colno}, char {e.pos}')
    print(f'Error message: {e.msg}')
    # Show context around the error
    lines = content.split('\n')
    if e.lineno <= len(lines):
        start = max(0, e.lineno - 3)
        end = min(len(lines), e.lineno + 2)
        print(f'\nContext (lines {start+1}-{end+1}):')
        for i in range(start, end):
            marker = '>>>' if i == e.lineno - 1 else '   '
            print(f'{marker} {i+1}: {lines[i]}')
