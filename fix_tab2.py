path = 'TerminalSimulation.Avalonia/MainWindow.axaml'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

target = '''                    </TabControl>
                </Grid>
            </Grid>

            <!--
                Dynamic tabs from OpenedUtilityTabs are added/removed in code-behind'''

tab_xml = '''
                        <!-- Static tab: HTTP Debug -->
                        <TabItem Classes="BrowserTab">
                            <TabItem.Header>
                                <StackPanel Orientation="Horizontal">
                                    <materialIcons:MaterialIcon Kind="Web" Width="16" Height="16" Margin="0,0,8,0" VerticalAlignment="Center" Foreground="{DynamicResource SukiText}"/>
                                    <TextBlock Text="HTTP ต๗สิ" VerticalAlignment="Center" FontWeight="SemiBold" Foreground="{DynamicResource SukiText}"/>
                                </StackPanel>
                            </TabItem.Header>

                            <Border Classes="TabContentOuter">
                                <suki:GlassCard Classes="TabContentInner">
                                    <local:HttpRequesterControl DataContext="{Binding HttpRequesterVM}" />
                                </suki:GlassCard>
                            </Border>
                        </TabItem>
                    </TabControl>
                </Grid>
            </Grid>

            <!--
                Dynamic tabs from OpenedUtilityTabs are added/removed in code-behind'''

# Also add converter
content = content.replace('{StaticResource IntEqualsVisibilityConverter}', '{x:Static converters:SharedConverters.IntEqualsVisibilityConverter}')

content = content.replace(target, tab_xml)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
