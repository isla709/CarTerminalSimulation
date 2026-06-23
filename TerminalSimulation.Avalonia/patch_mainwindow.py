import os

path = "MainWindow.axaml"
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Replace StaticResource with x:Static
content = content.replace('{StaticResource IntEqualsVisibilityConverter}', '{x:Static converters:SharedConverters.IntEqualsVisibilityConverter}')

# Inject the HTTP Debug tab before </TabControl>
tab_xml = """
                        <!-- Static tab: HTTP 调试 -->
                        <TabItem Classes="BrowserTab">
                            <TabItem.Header>
                                <StackPanel Orientation="Horizontal">
                                    <materialIcons:MaterialIcon Kind="Web" Width="16" Height="16"
                                                                 Margin="0,0,8,0" VerticalAlignment="Center" Foreground="{DynamicResource SukiText}"/>
                                    <TextBlock Text="HTTP 调试" VerticalAlignment="Center" FontWeight="SemiBold" Foreground="{DynamicResource SukiText}"/>
                                </StackPanel>
                            </TabItem.Header>

                            <Border Classes="TabContentOuter">
                                <suki:GlassCard Classes="TabContentInner">
                                    <local:HttpRequesterControl DataContext="{Binding HttpRequesterVM}" />
                                </suki:GlassCard>
                            </Border>
                        </TabItem>
                    </TabControl>"""

content = content.replace('                    </TabControl>', tab_xml)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
