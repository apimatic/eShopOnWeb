with open('.opencode/skills/maxio-getting-started/map/models/records-3-Of-Su.md') as f:
    for i, line in enumerate(f, 1):
        if '| `Subscription` ' in line or '| `Subscription`|' in line:
            print(i, line.strip()[:350])
