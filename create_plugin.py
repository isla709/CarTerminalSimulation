import os
import sys

def create_plugin(plugin_name):
    base_dir = os.path.dirname(os.path.abspath(__file__))
    plugin_dir = os.path.join(base_dir, f"TerminalSimulation.Plugins.{plugin_name}")
    
    if os.path.exists(plugin_dir):
        print(f"Error: Directory {plugin_dir} already exists.")
        sys.exit(1)
        
    os.makedirs(plugin_dir)
    print(f"Created directory: {plugin_dir}")
    
    # Create .csproj
    csproj_path = os.path.join(plugin_dir, f"TerminalSimulation.Plugins.{plugin_name}.csproj")
    csproj_content = f"""<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\\TerminalSimulation.PluginBase\\TerminalSimulation.PluginBase.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <PackageReference Include="MaterialDesignThemes" Version="5.3.2" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <!-- 直接将编译输出的扩展名设置为 .Plugin -->
    <TargetExt>.Plugin</TargetExt>
  </PropertyGroup>

</Project>
"""
    with open(csproj_path, 'w', encoding='utf-8') as f:
        f.write(csproj_content)
    print(f"Created project file: {csproj_path}")
    
    # Create Plugin Class
    class_path = os.path.join(plugin_dir, f"{plugin_name}Plugin.cs")
    class_content = f"""using System;
using System.Windows;
using System.Windows.Controls;
using TerminalSimulation.PluginBase;

namespace TerminalSimulation.Plugins.{plugin_name}
{{
    public class {plugin_name}Plugin : IPlugin
    {{
        public string Id => "{os.urandom(16).hex()}";
        public string Name => "{plugin_name}";
        public string Description => "A custom plugin for Terminal Simulation";
        public string Author => "Your Name";
        public string Version => "1.0.0";
        public PluginLocation Location => PluginLocation.Utility; // Or MainTab
        public bool AllowMultipleInstances => false;
        public string IconKind => "Puzzle"; // MaterialDesign icon kind

        private IPluginContext? _context;

        public void Initialize(IPluginContext context)
        {{
            _context = context;
            _context.Log($"{Name} loaded successfully!");
        }}

        public FrameworkElement GetConfigurationPanel()
        {{
            // Return your custom WPF UI here
            return new TextBlock 
            {{ 
                Text = "Hello from {plugin_name}!",
                Margin = new Thickness(20),
                FontSize = 16
            }};
        }}
    }}
}}
"""
    with open(class_path, 'w', encoding='utf-8') as f:
        f.write(class_content)
    print(f"Created plugin class: {class_path}")
    
    # Update .slnx
    slnx_path = os.path.join(base_dir, "TerminalSimulation.slnx")
    if os.path.exists(slnx_path):
        with open(slnx_path, 'r', encoding='utf-8') as f:
            lines = f.readlines()
            
        insert_index = -1
        for i, line in enumerate(lines):
            if line.strip() == "</Solution>":
                insert_index = i
                break
                
        if insert_index != -1:
            project_line = f'  <Project Path="TerminalSimulation.Plugins.{plugin_name}/TerminalSimulation.Plugins.{plugin_name}.csproj" />\n'
            lines.insert(insert_index, project_line)
            
            # Sort the project lines to keep it neat (optional, but good practice)
            # Find start of projects
            proj_start = -1
            for i, line in enumerate(lines):
                if "<Project " in line:
                    proj_start = i
                    break
            
            if proj_start != -1:
                proj_end = insert_index + 1
                projs = lines[proj_start:proj_end]
                projs.sort()
                lines = lines[:proj_start] + projs + lines[proj_end:]
                
            with open(slnx_path, 'w', encoding='utf-8') as f:
                f.writelines(lines)
            print(f"Added project to {slnx_path}")
            
    print(f"\\nSuccessfully created plugin: {plugin_name}")
    print("Please run `dotnet build` to compile the new plugin.")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python create_plugin.py <PluginName>")
        print("Example: python create_plugin.py MyAwesomePlugin")
        sys.exit(1)
        
    create_plugin(sys.argv[1])
