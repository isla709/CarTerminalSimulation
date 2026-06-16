import sys
import subprocess

def main():
    # 1. Get the old MainWindow.xaml content from git
    result = subprocess.run(['git', 'show', '8f66b6c:TerminalSimulation.Wpf/MainWindow.xaml'], capture_output=True, text=True, encoding='utf-8')
    old_xaml = result.stdout
    
    # Extract Utilities View
    start_str = '<!-- Utilities View -->'
    end_str = '<FrameworkElement x:Name="DummyDynamicTabs" Visibility="Collapsed" />\n\n        </Grid>'
    
    start_idx = old_xaml.find(start_str)
    end_idx = old_xaml.find(end_str)
    
    if start_idx == -1 or end_idx == -1:
        print("Could not extract Utilities View from old commit")
        return
        
    utilities_view_xml = old_xaml[start_idx:end_idx + len('<FrameworkElement x:Name="DummyDynamicTabs" Visibility="Collapsed" />\n')]
    
    # 2. Modify Utilities View XML
    # Remove PopupBox
    popup_start = utilities_view_xml.find('<materialDesign:PopupBox')
    if popup_start != -1:
        popup_end = utilities_view_xml.find('</materialDesign:PopupBox>') + len('</materialDesign:PopupBox>')
        utilities_view_xml = utilities_view_xml[:popup_start] + utilities_view_xml[popup_end:]
        
    # Change DynamicUtilityTabs to UtilityPlugins
    utilities_view_xml = utilities_view_xml.replace('DataContext.DynamicUtilityTabs', 'DataContext.UtilityPlugins')
    
    # Modify ItemTemplate
    old_item_template = '''<TabControl.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Orientation="Horizontal">
                            <materialDesign:PackIcon Kind="{Binding IconKind}" Width="16" Height="16" Margin="0,0,8,0" VerticalAlignment="Center"/>
                            <TextBlock Text="{Binding Title}" VerticalAlignment="Center"/>
                            <Button Command="{Binding CloseCommand}" Visibility="{Binding IsClosable, Converter={StaticResource BooleanToVisibilityConverter}}" Style="{StaticResource MaterialDesignToolButton}" Width="20" Height="20" Margin="8,0,-8,0">
                                <materialDesign:PackIcon Kind="Close" Width="12" Height="12" />
                            </Button>
                        </StackPanel>
                    </DataTemplate>
                </TabControl.ItemTemplate>'''
                
    new_item_template = '''<TabControl.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Orientation="Horizontal">
                            <materialDesign:PackIcon Kind="Puzzle" Width="16" Height="16" Margin="0,0,8,0" VerticalAlignment="Center"/>
                            <TextBlock Text="{Binding Name}" VerticalAlignment="Center"/>
                        </StackPanel>
                    </DataTemplate>
                </TabControl.ItemTemplate>'''
    
    utilities_view_xml = utilities_view_xml.replace(old_item_template, new_item_template)
    
    # Add DataTemplate for Content in Resources
    old_resources = '<TabControl.Resources>'
    new_resources = '''<TabControl.Resources>
                    <DataTemplate DataType="{x:Type plugin:IPlugin}">
                        <ContentControl Content="{Binding Converter={StaticResource PluginToContentConverter}}" />
                    </DataTemplate>'''
    utilities_view_xml = utilities_view_xml.replace(old_resources, new_resources)
    
    # Add namespace alias plugin:IPlugin if needed, wait we can just add xmlns:plugin="clr-namespace:TerminalSimulation.PluginBase;assembly=TerminalSimulation.PluginBase"
    # We will add it to the top of MainWindow.xaml
    
    # 3. Apply to current MainWindow.xaml
    with open('TerminalSimulation.Wpf/MainWindow.xaml', 'r', encoding='utf-8') as f:
        current_xaml = f.read()
        
    # Fix the MainPlugins tab
    current_xaml = current_xaml.replace('<TextBlock Text="实用工具插件" VerticalAlignment="Center"/>', '<TextBlock Text="插件功能" VerticalAlignment="Center"/>')
    current_xaml = current_xaml.replace('ItemsSource="{Binding Plugins}"', 'ItemsSource="{Binding MainPlugins}"')
    
    # Insert Utilities View
    insert_point = current_xaml.find('</Grid> <!-- Closes Normal Main Content Wrapper -->')
    if insert_point != -1:
        insert_point += len('</Grid> <!-- Closes Normal Main Content Wrapper -->')
        current_xaml = current_xaml[:insert_point] + '\n\n        ' + utilities_view_xml + current_xaml[insert_point:]
        
    # Ensure xmlns:plugin exists
    if 'xmlns:plugin="clr-namespace:TerminalSimulation.PluginBase' not in current_xaml:
        ns_insert = current_xaml.find('xmlns:vlc=')
        if ns_insert != -1:
            end_line = current_xaml.find('\n', ns_insert)
            current_xaml = current_xaml[:end_line] + '\n        xmlns:plugin="clr-namespace:TerminalSimulation.PluginBase;assembly=TerminalSimulation.PluginBase"' + current_xaml[end_line:]
            
    with open('TerminalSimulation.Wpf/MainWindow.xaml', 'w', encoding='utf-8') as f:
        f.write(current_xaml)

if __name__ == "__main__":
    main()
