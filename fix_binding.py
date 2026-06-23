import re

path = 'TerminalSimulation.Avalonia/HttpRequesterControl.axaml'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Replace ReflectionBinding with Binding, and add x:CompileBindings="False"
content = content.replace('{ReflectionBinding #HttpRequesterRoot.DataContext.RemoveParamCommand}', '{Binding $parent[UserControl].DataContext.RemoveParamCommand}')
content = content.replace('{ReflectionBinding #HttpRequesterRoot.DataContext.RemoveHeaderCommand}', '{Binding $parent[UserControl].DataContext.RemoveHeaderCommand}')
content = content.replace('{ReflectionBinding #HttpRequesterRoot.DataContext.RemoveFormDataCommand}', '{Binding $parent[UserControl].DataContext.RemoveFormDataCommand}')
content = content.replace('{ReflectionBinding #HttpRequesterRoot.DataContext.RemoveFormUrlEncodedCommand}', '{Binding $parent[UserControl].DataContext.RemoveFormUrlEncodedCommand}')

# Add x:CompileBindings="False" to the Button
content = content.replace('<Button Grid.Column="3" Command="{Binding $parent', '<Button Grid.Column="3" x:CompileBindings="False" Command="{Binding $parent')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
