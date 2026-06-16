import re

def main():
    with open('TerminalSimulation.Wpf/ViewModels/MainViewModel.cs', 'r', encoding='utf-8') as f:
        text = f.read()

    # Remove DynamicUtilityTabs
    text = re.sub(r'^\s*public\s+ObservableCollection<TerminalSimulation\.Wpf\.ViewModels\.Utilities\.UtilityTabViewModelBase>\s+DynamicUtilityTabs\s*\{\s*get;\s*\}\s*=\s*new\(\);\s*\n', '', text, flags=re.MULTILINE)

    # Remove AddStreamTab method
    start_idx = text.find('private void AddStreamTab()')
    if start_idx != -1:
        # Also remove the [RelayCommand] above it
        attr_start = text.rfind('[RelayCommand]', 0, start_idx)
        if attr_start != -1 and attr_start > start_idx - 50:
            start_idx = attr_start

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

if __name__ == '__main__':
    main()
