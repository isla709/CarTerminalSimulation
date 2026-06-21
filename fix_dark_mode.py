import re

file_path = r'TerminalSimulation.Avalonia\MainWindow.axaml'
with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Fix RootBlurredImage Background
content = content.replace('<Border Background="White" Opacity="{Binding BackgroundOpacity}" />', 
                          '<Border Background="{DynamicResource SystemRegionBrush}" Opacity="{Binding BackgroundOpacity}" />')

# 2. Fix Hardcoded Foregrounds
content = re.sub(r'Foreground="Gray"', 'Foreground="{DynamicResource SystemControlForegroundBaseMediumBrush}"', content)
content = re.sub(r'Foreground="#666"', 'Foreground="{DynamicResource SystemControlForegroundBaseMediumBrush}"', content)
content = re.sub(r'Foreground="#333"', 'Foreground="{DynamicResource SystemControlForegroundBaseMediumBrush}"', content)
content = re.sub(r'Foreground="#888888"', 'Foreground="{DynamicResource SystemControlForegroundBaseMediumBrush}"', content)
content = re.sub(r'Foreground="#D4D4D4"', 'Foreground="{DynamicResource SystemControlForegroundBaseHighBrush}"', content)

# 3. Fix LogListBox and MapWebView hardcoded backgrounds
content = content.replace('Background="#1E1E1E"', 'Background="Transparent"')
content = content.replace('Background="#222222"', 'Background="Transparent"')

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)
print('MainWindow fixed!')
