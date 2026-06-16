import sys
import subprocess

def main():
    with open('TerminalSimulation.Wpf/MainWindow.xaml', 'r', encoding='utf-8') as f:
        content = f.read()
        
    # Find the Utilities TabControl
    tab_ctrl_start = content.find('<!-- Utilities View -->')
    if tab_ctrl_start == -1:
        print("Utilities View not found")
        return
        
    # Replace ItemsSource binding
    content = content.replace(
        '<CollectionContainer Collection="{Binding Source={x:Reference DummyDynamicTabs}, Path=DataContext.UtilityPlugins}" />',
        '<CollectionContainer Collection="{Binding Source={x:Reference DummyDynamicTabs}, Path=DataContext.OpenedUtilityTabs}" />'
    )
    
    # Restore the PopupBox
    # We need to insert it after TabPanel
    tab_panel_str = '<TabPanel x:Name="HeaderPanel" Grid.Column="0" Panel.ZIndex="1" IsItemsHost="True" KeyboardNavigation.TabIndex="1" VerticalAlignment="Bottom"/>'
    insert_idx = content.find(tab_panel_str)
    
    if insert_idx != -1 and '<materialDesign:PopupBox' not in content[insert_idx:insert_idx+500]:
        popup_xml = '''
                                <materialDesign:PopupBox Grid.Column="1" Width="28" Height="28" Margin="4,0,0,4" VerticalAlignment="Bottom" ToolTip="添加工具" PlacementMode="BottomAndAlignLeftEdges">
                                    <materialDesign:PopupBox.ToggleContent>
                                        <Border Background="{DynamicResource MaterialDesignPaper}" CornerRadius="14" Opacity="0.8" Width="28" Height="28">
                                            <materialDesign:PackIcon Kind="Plus" Foreground="{DynamicResource MaterialDesignBodyLight}" HorizontalAlignment="Center" VerticalAlignment="Center" />
                                        </Border>
                                    </materialDesign:PopupBox.ToggleContent>
                                    <StackPanel Margin="8">
                                        <ItemsControl ItemsSource="{Binding UtilityPlugins}">
                                            <ItemsControl.ItemTemplate>
                                                <DataTemplate>
                                                    <Button Content="{Binding Name}" Command="{Binding DataContext.OpenUtilityPluginCommand, RelativeSource={RelativeSource AncestorType=Window}}" CommandParameter="{Binding}" Style="{StaticResource MaterialDesignFlatButton}" HorizontalAlignment="Left" />
                                                </DataTemplate>
                                            </ItemsControl.ItemTemplate>
                                        </ItemsControl>
                                    </StackPanel>
                                </materialDesign:PopupBox>'''
        content = content[:insert_idx + len(tab_panel_str)] + popup_xml + content[insert_idx + len(tab_panel_str):]
    
    # Modify the ItemTemplate
    old_item_template = '''<TabControl.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Orientation="Horizontal">
                            <materialDesign:PackIcon Kind="Puzzle" Width="16" Height="16" Margin="0,0,8,0" VerticalAlignment="Center"/>
                            <TextBlock Text="{Binding Name}" VerticalAlignment="Center"/>
                        </StackPanel>
                    </DataTemplate>
                </TabControl.ItemTemplate>'''
                
    new_item_template = '''<TabControl.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Orientation="Horizontal">
                            <materialDesign:PackIcon Kind="{Binding IconKind}" Width="16" Height="16" Margin="0,0,8,0" VerticalAlignment="Center"/>
                            <TextBlock Text="{Binding Title}" VerticalAlignment="Center"/>
                            <Button Command="{Binding CloseCommand}" Style="{StaticResource MaterialDesignToolButton}" Width="20" Height="20" Margin="8,0,-8,0">
                                <materialDesign:PackIcon Kind="Close" Width="12" Height="12" />
                            </Button>
                        </StackPanel>
                    </DataTemplate>
                </TabControl.ItemTemplate>'''
                
    if old_item_template in content:
        content = content.replace(old_item_template, new_item_template)
    else:
        print("Warning: old item template not found!")
        
    # Modify the DataType in Resources
    old_data_type = 'DataType="{x:Type plugin:IPlugin}"'
    # we need xmlns:plugins_vm="clr-namespace:TerminalSimulation.Wpf.ViewModels.Plugins"
    if 'xmlns:plugins_vm=' not in content:
        ns_insert = content.find('xmlns:vlc=')
        if ns_insert != -1:
            end_line = content.find('\n', ns_insert)
            content = content[:end_line] + '\n        xmlns:plugins_vm="clr-namespace:TerminalSimulation.Wpf.ViewModels.Plugins"' + content[end_line:]
            
    content = content.replace(old_data_type, 'DataType="{x:Type plugins_vm:OpenedPluginTab}"')
    
    with open('TerminalSimulation.Wpf/MainWindow.xaml', 'w', encoding='utf-8') as f:
        f.write(content)
        
if __name__ == "__main__":
    main()
