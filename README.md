# Shirobot.Plugin.MyList

`Shirobot.Plugin.MyList` 基于 [Shirobot](https://github.com/greepar/shirobot) 的插件项目。

## 如何部署开发环境

推荐目录结构：

- `./RiderProjects/Shirobot.Plugin.MyList`
- `./RiderProjects/Shirobot`

如果你的本地仓库布局不同，还需要修改 `csproj` 里的这些属性：

- `HostProjectRoot`
- `HostExe`
- `HostPluginDir`
- `ProjectReference Include="..\\..\\Shirobot\\ShiroBot.SDK\\ShiroBot.SDK.csproj"`

其中：

- `HostExe` 需要指向你本机实际使用的 `ShiroBot.exe`
- `HostPluginDir` 需要指向你本机实际使用的插件输出目录，通常是 `ShiroBot.exe` 所在目录下的 `plugins\\Shirobot.Plugin.MyList\\`

开发流程：

1. 打开项目 [Shirobot.Plugin.MyList.csproj](C:\Users\JustMe\RiderProjects\Shirobot.Plugin.MyList\Shirobot.Plugin.MyList\Shirobot.Plugin.MyList.csproj)
2. 修改插件代码或 `Assets/config.toml`
3. 执行 `dotnet build`
4. Debug 构建后插件会自动复制到 `ShiroBot` 的插件目录
5. 启动 `ShiroBot.exe` 进行调试

如果 `ShiroBot.exe` 正在运行，构建后的复制步骤可能会因为 DLL 被占用而失败。这种情况下先停止宿主，再重新构建。

## 运行方式

运行本项目需要使用 [Shirobot](https://github.com/greepar/shirobot)。

下载 `Shirobot` 后，将本项目构建产物放到其 `plugins/Shirobot.Plugin.MyList/` 目录下即可加载。

本项目规范 `Shirobot` Plugin 开发流程 。
