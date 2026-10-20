import fs from 'fs';
import path from 'path';
import { spawnSync } from 'child_process';
import { hvigor, HvigorPlugin } from '@ohos/hvigor';

// DevEco invokes FlutterTask directly, bypassing the PowerShell entry point.
// Generate the same encrypted asset immediately before that task runs.
export function mapCredentialPlugin(projectRoot: string): HvigorPlugin {
  const output = path.join(projectRoot, 'assets/credentials/map-credential.bin');
  let generated = false;
  function clean() {
    if (generated && fs.existsSync(output)) fs.unlinkSync(output);
    generated = false;
  }
  function prepare() {
    let repository = projectRoot;
    while (!fs.existsSync(path.join(repository, '.git'))) {
      const parent = path.dirname(repository);
      if (parent === repository) throw new Error('CTS repository not found. Use tool/Build.ps1 first.');
      repository = parent;
    }
    const relative = path.relative(repository, projectRoot).replace(/\\/g, '/');
    if (!/^bin\/(Debug|Release)\/Flutter\/ohos(?:-arm|-x64)?\/source$/.test(relative)) {
      throw new Error('Open the generated OHOS project under bin/Debug/Flutter after tool/Build.ps1.');
    }
    generated = true;
    clean();
    generated = true;
    try {
      const input = path.join(repository, 'docs/KEY.txt');
      const ignored = spawnSync('git', ['-C', repository, 'check-ignore', '--quiet', '--', 'docs/KEY.txt']);
      const tracked = spawnSync('git', ['-C', repository, 'ls-files', '--error-unmatch', '--', 'docs/KEY.txt']);
      if (ignored.status !== 0 || tracked.status !== 1 || !fs.existsSync(input)) throw new Error();
      const properties = fs.readFileSync(path.join(projectRoot, 'ohos/local.properties'), 'utf8');
      const rawSdk = properties.match(/^flutter\.sdk=(.+)$/m)?.[1]?.trim();
      if (!rawSdk) throw new Error();
      const sdk = rawSdk.replace(/\\\\/g, '\\').replace(/\\:/g, ':');
      const dart = path.join(sdk, 'bin/cache/dart-sdk/bin', process.platform === 'win32' ? 'dart.exe' : 'dart');
      const result = spawnSync(dart, [
        `--packages=${path.join(projectRoot, '.dart_tool/package_config.json')}`,
        path.join(projectRoot, 'tool/embed_map_credentials.dart'), input, output,
      ], { cwd: projectRoot, stdio: 'pipe' });
      if (result.status !== 0 || !fs.existsSync(output)) throw new Error();
      console.info('CTS embedded map credential prepared for DevEco build.');
    } catch (_) {
      clean();
      // Never forward child errors, credential contents or command arguments.
      throw new Error('CTS map credential generation failed. Check local untracked docs/KEY.txt and run tool/Build.ps1.');
    }
  }
  return {
    pluginId: 'cts-map-credential',
    apply(root) {
      hvigor.nodesEvaluated(() => {
        const entry = root.getSubNodeByName('entry');
        entry?.getAllTasks().filter(task => task.getName().endsWith('@FlutterTask')).forEach(task => {
          task.beforeRun(prepare);
          task.afterRun(clean);
        });
      });
      hvigor.buildFinished(clean);
    },
  };
}
