import os, glob
for root, dirs, files in os.walk(os.environ.get('TEMP', r'C:\Windows\Temp')):
    if 'advanced-billing-sample-sdk' in dirs:
        print(os.path.join(root, 'advanced-billing-sample-sdk', 'Models', 'CreateSubscription.cs'))
        break
    # limit depth by pruning
    dirs[:] = [d for d in dirs if d not in ('node_modules','.git')]
