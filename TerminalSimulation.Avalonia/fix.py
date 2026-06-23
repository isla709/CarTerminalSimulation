import os
path = 'HttpRequesterControl.axaml'
with open(path, 'rb') as f:
    content = f.read().decode('mbcs', errors='ignore')
content = content.replace('Content="发? Command="', 'Content="发送" Command="')
with open(path, 'w', encoding='utf-8') as f:
    f.write(content)

path2 = 'MainWindow.axaml'
with open(path2, 'rb') as f:
    content2 = f.read().decode('mbcs', errors='ignore')
with open(path2, 'w', encoding='utf-8') as f:
    f.write(content2)
