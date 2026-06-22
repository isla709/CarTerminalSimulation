import sys

filepath = r'D:\Project\VS\CarTerminalSimulation\TerminalSimulation.Avalonia\MainWindow.axaml'
with open(filepath, 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Add x:Name to VideoTabItem
old_tab_header = '<TabItem Header="视频控制">'
new_tab_header = '<TabItem x:Name="VideoTabItem" Header="视频控制">'
content = content.replace(old_tab_header, new_tab_header)

# 2. Extract the grid
start_idx = content.find('<Grid Margin="16">', content.find(new_tab_header))
end_idx = content.find('</TabItem>', start_idx)

inner_grid = content[start_idx : content.rfind('</Grid>', start_idx, end_idx) + 7]

content = content[:start_idx] + '<Grid />\n                        ' + content[content.rfind('</Grid>', start_idx, end_idx) + 7:]

# 3. Add the extracted grid to MapWebViewContainer
map_container = '''<Border x:Name="MapWebViewContainer" Background="Transparent"
                            IsVisible="{Binding #MapTabItem.IsSelected}"
                            Margin="0,48,0,0"
                            HorizontalAlignment="Stretch" VerticalAlignment="Stretch" />'''

new_video_container = f'''

                    <!-- Floating Video Container to prevent VideoView detachment -->
                    <Border x:Name="VideoTabContainer" Background="Transparent"
                            IsVisible="{{Binding #VideoTabItem.IsSelected}}"
                            Margin="0,48,0,0"
                            HorizontalAlignment="Stretch" VerticalAlignment="Stretch">
                        {inner_grid}
                    </Border>'''

content = content.replace(map_container, map_container + new_video_container)

with open(filepath, 'w', encoding='utf-8') as f:
    f.write(content)
print('Done!')
