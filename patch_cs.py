import re

def main():
    # Fix MainViewModel.cs
    with open('TerminalSimulation.Wpf/ViewModels/MainViewModel.cs', 'r', encoding='utf-8') as f:
        text = f.read()

    # Remove the Plugins and OnLocationReporting fields
    text = re.sub(r'^\s*public\s+System\.Collections\.ObjectModel\.ObservableCollection<PluginViewModel>\s+Plugins\s*{\s*get;\s*}\s*=\s*new\(\);\s*\n', '', text, flags=re.MULTILINE)
    text = re.sub(r'^\s*public\s+event\s+Action<System\.Collections\.Generic\.List<byte>>\s+OnLocationReporting;\s*\n', '', text, flags=re.MULTILINE)

    # Remove the duplicate LoadPlugins method
    load_plugins_pattern = r'\s*private\s+void\s+LoadPlugins\(\)\s*\{.*?(?=\s*(?:private|public|protected|internal|//|$))'
    # Wait, regex for method body is tricky, let's just search and remove using string ops.
    start_idx = text.find('private void LoadPlugins()')
    if start_idx != -1:
        # Find matching brace
        brace_count = 0
        end_idx = -1
        in_body = False
        for i in range(start_idx, len(text)):
            if text[i] == '{':
                brace_count += 1
                in_body = True
            elif text[i] == '}':
                brace_count -= 1
                if in_body and brace_count == 0:
                    end_idx = i + 1
                    break
        if end_idx != -1:
            text = text[:start_idx] + text[end_idx:]

    with open('TerminalSimulation.Wpf/ViewModels/MainViewModel.cs', 'w', encoding='utf-8') as f:
        f.write(text)

    # Fix MainWindow.xaml namespaces
    with open('TerminalSimulation.Wpf/MainWindow.xaml', 'r', encoding='utf-8') as f:
        xaml = f.read()

    xaml = re.sub(r'\s*xmlns:utils="clr-namespace:TerminalSimulation\.Wpf\.Views\.Utilities"', '', xaml)
    xaml = re.sub(r'\s*xmlns:vmut="clr-namespace:TerminalSimulation\.Wpf\.ViewModels\.Utilities"', '', xaml)

    with open('TerminalSimulation.Wpf/MainWindow.xaml', 'w', encoding='utf-8') as f:
        f.write(xaml)

if __name__ == '__main__':
    main()
