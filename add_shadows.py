import re

file_path = r'TerminalSimulation.Avalonia\MainWindow.axaml'
with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

def replacer(match):
    tag_content = match.group(1)
    
    margin_match = re.search(r'Margin=\'([^\']*)\'|Margin=\"([^\"]*)\"', tag_content)
    margin = margin_match.group(1) or margin_match.group(2) if margin_match else '0'
    
    grid_row_match = re.search(r'Grid\.Row=\'([^\']*)\'|Grid\.Row=\"([^\"]*)\"', tag_content)
    grid_row = grid_row_match.group(1) or grid_row_match.group(2) if grid_row_match else None
    
    grid_col_match = re.search(r'Grid\.Column=\'([^\']*)\'|Grid\.Column=\"([^\"]*)\"', tag_content)
    grid_col = grid_col_match.group(1) or grid_col_match.group(2) if grid_col_match else None

    grid_colspan_match = re.search(r'Grid\.ColumnSpan=\'([^\']*)\'|Grid\.ColumnSpan=\"([^\"]*)\"', tag_content)
    grid_colspan = grid_colspan_match.group(1) or grid_colspan_match.group(2) if grid_colspan_match else None
    
    new_tag = tag_content
    new_tag = re.sub(r'\s*Margin=[\'\"][^\'\"]*[\'\"]', '', new_tag)
    new_tag = re.sub(r'\s*Grid\.Row=[\'\"][^\'\"]*[\'\"]', '', new_tag)
    new_tag = re.sub(r'\s*Grid\.Column=[\'\"][^\'\"]*[\'\"]', '', new_tag)
    new_tag = re.sub(r'\s*Grid\.ColumnSpan=[\'\"][^\'\"]*[\'\"]', '', new_tag)
    
    outer_attrs = f'Margin="{margin}"'
    if grid_row: outer_attrs += f' Grid.Row="{grid_row}"'
    if grid_col: outer_attrs += f' Grid.Column="{grid_col}"'
    if grid_colspan: outer_attrs += f' Grid.ColumnSpan="{grid_colspan}"'
    
    return f'<Border BoxShadow="0 2 12 0 #15000000" CornerRadius="12" {outer_attrs}>\n<suki:GlassCard {new_tag} Margin="0" BorderThickness="1" BorderBrush="{{DynamicResource SystemControlForegroundBaseLowBrush}}" CornerRadius="12">'

content = re.sub(r'<suki:GlassCard([^>]*)>', replacer, content)
content = content.replace('</suki:GlassCard>', '</suki:GlassCard>\n</Border>')

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)
print('Done!')
