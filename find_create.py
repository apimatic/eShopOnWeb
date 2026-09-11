with open('.opencode/skills/maxio-getting-started/map/models/records-2-Cr-Ne.md') as f:
    for i, line in enumerate(f, 1):
        if "| `CreateSubscription`" in line:
            print(i, line.strip()[:350])
