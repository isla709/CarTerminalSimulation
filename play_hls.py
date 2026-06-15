import os
# 设置 vlc.dll 路径环境变量，指向我们项目自带的 libvlc
os.environ['PYTHON_VLC_MODULE_PATH'] = r"D:\Project\VS\CarTerminalSimulation\TerminalSimulation.Wpf\bin\Debug\net8.0-windows\win-x64\libvlc\win-x64"

import vlc
import time

url = "http://39.100.85.72:11078/hls/test110.m3u8"
print(f"正在使用 python-vlc 拉取流: {url}")

instance = vlc.Instance("--no-xlib")
player = instance.media_player_new()
media = instance.media_new(url)
player.set_media(media)

player.play()

print("开始播放... 按 Ctrl+C 退出")
try:
    while True:
        time.sleep(1)
except KeyboardInterrupt:
    player.stop()
    print("退出播放")
