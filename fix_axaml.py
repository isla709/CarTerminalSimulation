# -*- coding: utf-8 -*-
path = 'TerminalSimulation.Avalonia/HttpRequesterControl.axaml'

xml = """<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:materialIcons="using:Material.Icons.Avalonia"
             xmlns:suki="https://github.com/kikipoulet/SukiUI"
             xmlns:vm="using:TerminalSimulation.Avalonia.ViewModels.Utilities"
             xmlns:converters="using:TerminalSimulation.Avalonia.Converters"
             mc:Ignorable="d" d:DesignWidth="800" d:DesignHeight="600"
             x:Class="TerminalSimulation.Avalonia.HttpRequesterControl"
             x:Name="HttpRequesterRoot"
             x:DataType="vm:HttpRequesterViewModel">

    <Grid RowDefinitions="Auto,*,Auto" Margin="8">
        <!-- Top Request Bar -->
        <Grid ColumnDefinitions="Auto,*,Auto" Grid.Row="0" Margin="0,0,0,8">
            <ComboBox Grid.Column="0" ItemsSource="{Binding Methods}" SelectedItem="{Binding Method}" Width="100" Margin="0,0,8,0" CornerRadius="8" />
            <TextBox Grid.Column="1" Text="{Binding Url}" Watermark="Enter request URL" CornerRadius="8" />
            <Button Grid.Column="2" Content="发送" Command="{Binding SendRequestCommand}" Classes="Primary" CornerRadius="8" Width="80" Margin="8,0,0,0" />
        </Grid>
        
        <!-- Request Body/Params/Headers Tabs -->
        <TabControl Grid.Row="1" Margin="0,0,0,8" Padding="0">
            <TabItem Header="Params">
                <Grid RowDefinitions="Auto,*">
                    <StackPanel Orientation="Horizontal" Margin="0,4,0,4">
                        <Button Command="{Binding AddParamCommand}" Classes="Basic" Padding="4" ToolTip.Tip="添加参数">
                            <materialIcons:MaterialIcon Kind="Plus" />
                        </Button>
                    </StackPanel>
                    <ScrollViewer Grid.Row="1">
                        <ItemsControl ItemsSource="{Binding Params}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Grid ColumnDefinitions="Auto,*,*,Auto" Margin="0,2">
                                        <CheckBox IsChecked="{Binding IsEnabled}" Margin="0,0,8,0" />
                                        <TextBox Grid.Column="1" Text="{Binding Key}" Watermark="Key" Margin="0,0,8,0" />
                                        <TextBox Grid.Column="2" Text="{Binding Value}" Watermark="Value" Margin="0,0,8,0" />
                                        <Button Grid.Column="3" Command="{ReflectionBinding #HttpRequesterRoot.DataContext.RemoveParamCommand}" CommandParameter="{Binding}" Classes="Basic" Foreground="Red" Padding="4">
                                            <materialIcons:MaterialIcon Kind="Delete" />
                                        </Button>
                                    </Grid>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </ScrollViewer>
                </Grid>
            </TabItem>
            
            <TabItem Header="Headers">
                <Grid RowDefinitions="Auto,*">
                    <StackPanel Orientation="Horizontal" Margin="0,4,0,4">
                        <Button Command="{Binding AddHeaderCommand}" Classes="Basic" Padding="4" ToolTip.Tip="添加Header">
                            <materialIcons:MaterialIcon Kind="Plus" />
                        </Button>
                    </StackPanel>
                    <ScrollViewer Grid.Row="1">
                        <ItemsControl ItemsSource="{Binding Headers}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Grid ColumnDefinitions="Auto,*,*,Auto" Margin="0,2">
                                        <CheckBox IsChecked="{Binding IsEnabled}" Margin="0,0,8,0" />
                                        <TextBox Grid.Column="1" Text="{Binding Key}" Watermark="Key" Margin="0,0,8,0" />
                                        <TextBox Grid.Column="2" Text="{Binding Value}" Watermark="Value" Margin="0,0,8,0" />
                                        <Button Grid.Column="3" Command="{ReflectionBinding #HttpRequesterRoot.DataContext.RemoveHeaderCommand}" CommandParameter="{Binding}" Classes="Basic" Foreground="Red" Padding="4">
                                            <materialIcons:MaterialIcon Kind="Delete" />
                                        </Button>
                                    </Grid>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </ScrollViewer>
                </Grid>
            </TabItem>
            
            <TabItem Header="Body">
                <Grid RowDefinitions="Auto,*">
                    <StackPanel Orientation="Horizontal" Margin="0,4,0,8">
                        <RadioButton Content="none" IsChecked="{Binding BodyTypeIndex, Converter={x:Static converters:SharedConverters.IntEqualsVisibilityConverter}, ConverterParameter=0, Mode=OneWay}" GroupName="BodyType" Margin="0,0,16,0" Command="{Binding SetBodyTypeCommand}" CommandParameter="0"/>
                        <RadioButton Content="raw" IsChecked="{Binding BodyTypeIndex, Converter={x:Static converters:SharedConverters.IntEqualsVisibilityConverter}, ConverterParameter=1, Mode=OneWay}" GroupName="BodyType" Margin="0,0,16,0" Command="{Binding SetBodyTypeCommand}" CommandParameter="1"/>
                        <RadioButton Content="form-data" IsChecked="{Binding BodyTypeIndex, Converter={x:Static converters:SharedConverters.IntEqualsVisibilityConverter}, ConverterParameter=2, Mode=OneWay}" GroupName="BodyType" Margin="0,0,16,0" Command="{Binding SetBodyTypeCommand}" CommandParameter="2"/>
                        <RadioButton Content="x-www-form-urlencoded" IsChecked="{Binding BodyTypeIndex, Converter={x:Static converters:SharedConverters.IntEqualsVisibilityConverter}, ConverterParameter=3, Mode=OneWay}" GroupName="BodyType" Command="{Binding SetBodyTypeCommand}" CommandParameter="3"/>
                    </StackPanel>
                    
                    <Grid Grid.Row="1">
                        <!-- none -->
                        <TextBlock Text="This request does not have a body" Foreground="Gray" HorizontalAlignment="Center" VerticalAlignment="Center" IsVisible="{Binding BodyTypeIndex, Converter={x:Static converters:SharedConverters.IntEqualsVisibilityConverter}, ConverterParameter=0}" />
                        
                        <!-- raw -->
                        <Grid RowDefinitions="Auto,*" IsVisible="{Binding BodyTypeIndex, Converter={x:Static converters:SharedConverters.IntEqualsVisibilityConverter}, ConverterParameter=1}">
                            <ComboBox SelectedIndex="{Binding RawBodyTypeIndex}" Margin="0,0,0,8">
                                <ComboBoxItem Content="JSON" />
                                <ComboBoxItem Content="Text" />
                                <ComboBoxItem Content="XML" />
                                <ComboBoxItem Content="HTML" />
                            </ComboBox>
                            <TextBox Grid.Row="1" Text="{Binding RawBody}" AcceptsReturn="True" TextWrapping="Wrap" FontFamily="Consolas" />
                        </Grid>
                        
                        <!-- form-data -->
                        <Grid RowDefinitions="Auto,*" IsVisible="{Binding BodyTypeIndex, Converter={x:Static converters:SharedConverters.IntEqualsVisibilityConverter}, ConverterParameter=2}">
                            <Button Command="{Binding AddFormDataCommand}" Classes="Basic" Padding="4" Margin="0,0,0,8">
                                <materialIcons:MaterialIcon Kind="Plus" />
                            </Button>
                            <ScrollViewer Grid.Row="1">
                                <ItemsControl ItemsSource="{Binding FormData}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <Grid ColumnDefinitions="Auto,*,*,Auto" Margin="0,2">
                                                <CheckBox IsChecked="{Binding IsEnabled}" Margin="0,0,8,0" />
                                                <TextBox Grid.Column="1" Text="{Binding Key}" Watermark="Key" Margin="0,0,8,0" />
                                                <TextBox Grid.Column="2" Text="{Binding Value}" Watermark="Value" Margin="0,0,8,0" />
                                                <Button Grid.Column="3" Command="{ReflectionBinding #HttpRequesterRoot.DataContext.RemoveFormDataCommand}" CommandParameter="{Binding}" Classes="Basic" Foreground="Red" Padding="4">
                                                    <materialIcons:MaterialIcon Kind="Delete" />
                                                </Button>
                                            </Grid>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </ScrollViewer>
                        </Grid>

                        <!-- x-www-form-urlencoded -->
                        <Grid RowDefinitions="Auto,*" IsVisible="{Binding BodyTypeIndex, Converter={x:Static converters:SharedConverters.IntEqualsVisibilityConverter}, ConverterParameter=3}">
                            <Button Command="{Binding AddFormUrlEncodedCommand}" Classes="Basic" Padding="4" Margin="0,0,0,8">
                                <materialIcons:MaterialIcon Kind="Plus" />
                            </Button>
                            <ScrollViewer Grid.Row="1">
                                <ItemsControl ItemsSource="{Binding FormUrlEncodedData}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <Grid ColumnDefinitions="Auto,*,*,Auto" Margin="0,2">
                                                <CheckBox IsChecked="{Binding IsEnabled}" Margin="0,0,8,0" />
                                                <TextBox Grid.Column="1" Text="{Binding Key}" Watermark="Key" Margin="0,0,8,0" />
                                                <TextBox Grid.Column="2" Text="{Binding Value}" Watermark="Value" Margin="0,0,8,0" />
                                                <Button Grid.Column="3" Command="{ReflectionBinding #HttpRequesterRoot.DataContext.RemoveFormUrlEncodedCommand}" CommandParameter="{Binding}" Classes="Basic" Foreground="Red" Padding="4">
                                                    <materialIcons:MaterialIcon Kind="Delete" />
                                                </Button>
                                            </Grid>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </ScrollViewer>
                        </Grid>
                    </Grid>
                </Grid>
            </TabItem>
        </TabControl>
        
        <!-- Response Area (Fixed Height to prevent layout loops) -->
        <Border Grid.Row="2" Height="250" BorderBrush="{DynamicResource SukiBorderBrush}" BorderThickness="1" CornerRadius="8" Padding="8">
            <Grid RowDefinitions="Auto,*">
                <Grid ColumnDefinitions="Auto,*,Auto" Margin="0,0,0,8">
                    <TextBlock Text="Response" FontWeight="Bold" VerticalAlignment="Center" />
                    <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
                        <TextBlock Text="Status: " Foreground="Gray" />
                        <TextBlock Text="{Binding StatusCodeStr}" FontWeight="Bold" Margin="0,0,16,0" />
                        <TextBlock Text="Time: " Foreground="Gray" />
                        <TextBlock Text="{Binding TimeStr}" FontWeight="Bold" Margin="0,0,16,0" />
                        <TextBlock Text="Size: " Foreground="Gray" />
                        <TextBlock Text="{Binding SizeStr}" FontWeight="Bold" />
                    </StackPanel>
                </Grid>
                
                <TabControl Grid.Row="1" Padding="0">
                    <TabItem Header="Body">
                        <TextBox Text="{Binding ResponseBody}" IsReadOnly="True" AcceptsReturn="True" TextWrapping="Wrap" FontFamily="Consolas" BorderThickness="0" Background="Transparent" />
                    </TabItem>
                    <TabItem Header="Headers">
                        <ScrollViewer>
                            <ItemsControl ItemsSource="{Binding ResponseHeaders}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Grid ColumnDefinitions="Auto,*" Margin="0,2">
                                            <TextBlock Text="{Binding Key}" FontWeight="Bold" Width="200" />
                                            <TextBlock Grid.Column="1" Text="{Binding Value}" TextWrapping="Wrap" />
                                        </Grid>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </ScrollViewer>
                    </TabItem>
                </TabControl>
                
                <!-- Loading Overlay -->
                <Border Grid.RowSpan="2" Background="#80000000" IsVisible="{Binding IsSending}" CornerRadius="8">
                    <Border Background="{DynamicResource SukiCardBackground}" Width="150" Height="80" HorizontalAlignment="Center" VerticalAlignment="Center" CornerRadius="8" BorderBrush="{DynamicResource SukiBorderBrush}" BorderThickness="1">
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" VerticalAlignment="Center">
                            <TextBlock Text="Sending..." Margin="8,0,0,0" VerticalAlignment="Center" />
                        </StackPanel>
                    </Border>
                </Border>
            </Grid>
        </Border>
    </Grid>
</UserControl>"""

with open(path, 'w', encoding='utf-8') as f:
    f.write(xml)
