import re

def main():
    with open('TerminalSimulation.Wpf/MainWindow.xaml', 'r', encoding='utf-8') as f:
        text = f.read()

    # 1. Add converter
    text = text.replace('<converters:AspectRatioConverter x:Key="AspectRatioConverter" />', '<converters:AspectRatioConverter x:Key="AspectRatioConverter" />\n        <converters:PluginToContentConverter x:Key="PluginToContentConverter" />')

    # 2. Remove DataTemplate
    template_start = text.find('<DataTemplate DataType="{x:Type vmut:XunjieCloudStreamViewModel}">')
    if template_start != -1:
        template_end = text.find('</DataTemplate>', template_start) + len('</DataTemplate>')
        text = text[:template_start] + text[template_end:]

    # 3. Add TabItem to first TabControl
    new_tab = '''
                    <!-- 插件面板 -->
                    <TabItem>
                        <TabItem.Header>
                            <StackPanel Orientation="Horizontal">
                                <materialDesign:PackIcon Kind="Puzzle" Width="16" Height="16" Margin="0,0,8,0" VerticalAlignment="Center"/>
                                <TextBlock Text="实用工具插件" VerticalAlignment="Center"/>
                            </StackPanel>
                        </TabItem.Header>
                        <Grid Margin="8">
                            <ScrollViewer VerticalScrollBarVisibility="Auto">
                                <ItemsControl ItemsSource="{Binding Plugins}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <materialDesign:Card Margin="0,0,0,16" Padding="16">
                                                <StackPanel>
                                                    <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
                                                        <TextBlock Text="{Binding Name}" FontWeight="Bold" FontSize="18" Foreground="{DynamicResource MaterialDesign.Brush.Primary}" />
                                                        <TextBlock Text="{Binding Version}" Margin="8,0,0,0" VerticalAlignment="Bottom" Foreground="Gray" FontSize="12" />
                                                    </StackPanel>
                                                    <TextBlock Text="{Binding Description}" TextWrapping="Wrap" Margin="0,0,0,16" Foreground="Gray" FontSize="14" />
                                                    <ContentControl Content="{Binding Converter={StaticResource PluginToContentConverter}}" />
                                                </StackPanel>
                                            </materialDesign:Card>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </ScrollViewer>
                        </Grid>
                    </TabItem>
'''
    # Find the end of the first TabControl
    first_tc_end = text.find('</TabControl>')
    if first_tc_end != -1:
        text = text[:first_tc_end] + new_tab + text[first_tc_end:]

    # 4. Remove Utilities View
    # From <!-- Utilities View --> to the end
    util_start = text.find('<!-- Utilities View -->')
    if util_start != -1:
        # Keep </Grid> and </Window> at the end, but remove everything in between
        # The Utilities View is inside `<Grid x:Name="LayoutRoot">`, so its own <Grid> is what we remove.
        # Original:
        #         </Grid> <!-- Closes Normal Main Content Wrapper -->
        #         <!-- Utilities View -->
        #         <Grid ...> ... </Grid>
        #         <FrameworkElement ... />
        #         </Grid>
        #     </Grid>
        # </Window>
        
        # we will replace everything from <!-- Utilities View --> to the end of file with:
        text = text[:util_start] + "        </Grid>\n    </Grid>\n</Window>\n"

    with open('TerminalSimulation.Wpf/MainWindow.xaml', 'w', encoding='utf-8') as f:
        f.write(text)

if __name__ == '__main__':
    main()
