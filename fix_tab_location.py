import re

path = 'TerminalSimulation.Avalonia/MainWindow.axaml'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# First, remove the HTTP tab from MainTabControl
pattern = r'(\s*<!-- Static tab: HTTP µ÷ÊÔ -->\s*<TabItem Classes="BrowserTab">.*?<local:HttpRequesterControl DataContext="{Binding HttpRequesterVM}" />\s*</suki:GlassCard>\s*</Border>\s*</TabItem>)'
content = re.sub(pattern, '', content, flags=re.DOTALL)

# Also remove mangled versions if any
pattern_mangled = r'(\s*<!-- Static tab: HTTP è°ƒè¯• -->\s*<TabItem Classes="BrowserTab">.*?<local:HttpRequesterControl DataContext="{Binding HttpRequesterVM}" />\s*</suki:GlassCard>\s*</Border>\s*</TabItem>)'
content = re.sub(pattern_mangled, '', content, flags=re.DOTALL)

tab_xml = """
                        <!-- Static tab: HTTP µ÷ÊÔ -->
                        <TabItem Classes="BrowserTab">
                            <TabItem.Header>
                                <StackPanel Orientation="Horizontal">
                                    <materialIcons:MaterialIcon Kind="Web" Width="16" Height="16" Margin="0,0,8,0" VerticalAlignment="Center" Foreground="{DynamicResource SukiText}"/>
                                    <TextBlock Text="HTTP µ÷ÊÔ" VerticalAlignment="Center" FontWeight="SemiBold" Foreground="{DynamicResource SukiText}"/>
                                </StackPanel>
                            </TabItem.Header>

                            <Border Classes="TabContentOuter">
                                <suki:GlassCard Classes="TabContentInner">
                                    <local:HttpRequesterControl DataContext="{Binding HttpRequesterVM}" />
                                </suki:GlassCard>
                            </Border>
                        </TabItem>"""

# Find the UtilitiesTabControl closing tag and inject before it
# We know the second </TabControl> with 20 spaces is UtilitiesTabControl,
# but it's safer to find UtilitiesTabControl and inject before its closing tag.
# Let's split by '<TabControl x:Name="UtilitiesTabControl"'
parts = content.split('<TabControl x:Name="UtilitiesTabControl"')
if len(parts) == 2:
    sub_parts = parts[1].split('                    </TabControl>', 1)
    if len(sub_parts) == 2:
        parts[1] = sub_parts[0] + tab_xml + '\n                    </TabControl>' + sub_parts[1]
    content = parts[0] + '<TabControl x:Name="UtilitiesTabControl"' + parts[1]

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
