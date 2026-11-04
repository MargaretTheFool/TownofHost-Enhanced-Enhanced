[English](player-tags.en.md)

# 房主本地玩家标签

房主使用 `TOHEE-DATA/Tags/<好友码>.txt` 为玩家设置本地标签、权限和 GM 身份。标签显示在玩家名字前面，其他玩家通过房主发送的名字更新看到它。`TOHEE-DATA/tags.txt` 仅是低优先级的外观标签后备，不会设置权限或 GM 身份。文件都只保存在房主设备上。

这是有意变更的行为：`TOHEE-DATA/Moderators.txt` 和 `TOHEE-DATA/VIP-List.txt` 不再读取，也不再授予或显示 MOD/VIP 身份；相关名单选项已从界面移除，`/modcolor`、`/vipcolor` 命令也已移除。旧的 MOD/VIP 纯颜色文件不会授予身份或权限。`MOD_TAGS`、`VIP_TAGS` 现在只是普通文件夹名称，可用于存放结构化玩家配置。不会自动迁移旧配置，也不会自动删除这些旧文件；如需保留相关玩家的本地权限或 GM 设置，请手动将设置写入对应的 `<好友码>.txt`。模组内置的开发者标签（包括 DevUser）仍会显示，`Tags/SPONSOR_TAGS/<好友码>.txt` 的纯六位颜色格式仍可覆盖 DevUser 内置标签颜色。

## TOHE 单玩家 TXT：标签和权限

模组启动时会在 `TOHEE-DATA/Tags` 根目录建立三个模板；如果模板已存在，不会覆盖。桌面版路径相对于游戏工作目录；Android 版的单玩家文件位于应用的持久数据目录。这些模板只是供复制的示例文件，本身不会匹配玩家或授予任何权限：

| 模板 | 初始字段 |
| --- | --- |
| `Tag_Template.txt` | `TagName` 留空，`TagColor: FFFF00`，`PermissionLevel: 0`，所有布尔字段为 `false` |
| `Moderator_Template.txt` | `TagName: Moderator`、`TagColor: 8BBEE0`、`PermissionLevel: 5`，所有布尔字段为 `false` |
| `VIP_Template.txt` | `TagName: VIP`、`TagColor: FFFF00`、`PermissionLevel: 1`，所有布尔字段为 `false` |

复制所需模板，将副本改名为玩家的完整好友码加 `.txt`，再放到 `Tags` 根目录或直接子目录（一层）中。例如可复制 `Moderator_Template.txt` 到 `TOHEE-DATA/Tags/moderators/sampleuser#1234.txt`，或复制 `VIP_Template.txt` 到 `TOHEE-DATA/Tags/vips/sampleuser#1234.txt`，然后按需编辑字段。等级是累加的，因此 Moderator 模板的等级 5 也包含 `/start` 等较低等级权限；若不需要这些权限，请降低 `PermissionLevel`。模板文件自身不授予权限，只有按好友码命名的玩家配置文件会生效。

```text
TagName:
TagColor: FFFF00
PermissionLevel: 0
SayCommandAccess: false
EndCommandAccess: false
ExecuteCommandAccess: false
AssignGameMaster: false
```

`TagColor` 不带 `#`；六位十六进制颜色可设置单色。开启渐变标签选项时，也可写成两个颜色，如 `33CCFF FF99CC`。`TagColor` 可以留空；这样可单独使用 `TagName` 中的格式标记。`TagName` 可使用颜色、大小和字体标记，例如 `<color=#FF0000>R</color><size=150%>ain</size>bow`。启用“Enable Gradient Tags”时保留这些富文本格式；关闭时会去掉格式标记但保留可见文字。填写无效 `TagColor` 会回退为黄色。若希望标签和名字之间有空格，在 `TagName` 末尾保留空格。

## 使用 MAUL 标签生成器

可使用 [MAUL 标签生成器](https://moddedamong.us/tags.html) 创建 TOHEE 配置：选择 `TONE/TOHE:E`，输入玩家完整好友码，然后下载完整的 `.txt` 配置文件，并将其放入 `Tags` 根目录或一层普通分类子目录。页面上的“Copy generated tag”按钮只复制 `TagName` 中的标签标记，不会复制完整权限配置。生成器的渐变样式会把 `TMP` 标记放进 `TagName`，并将 `TagColor` 留空；纯色样式则会填写 `TagColor`。要保留内嵌的颜色、大小和字体格式，请启用“Enable Gradient Tags”；关闭后格式标记会被去除，但可见文字保留。若手动给含有内嵌颜色的 `TagName` 设置双颜色渐变 `TagColor`，自动渐变会重新为可见文字着色；要原样保留内嵌颜色，请将 `TagColor` 留空。生成器输出的布尔字段也可能使用 `y`/`n`，两者均可识别。

| `PermissionLevel` | 授权 |
| --- | --- |
| 0 | 不授予任何权限等级管理命令 |
| 1 | 大厅中为自己使用 `/rn` 改名、`/color` 改色 |
| 2 | 再加 `/warn` 警告、`/mid` 查看玩家列表、`/me <编号>` 查看其他玩家信息，以及为其他玩家请求 `/fix`；`/id` 玩家编号列表也可用，但等级 0–1 的玩家可在 `EnableVoteCommand` 选项开启时使用它 |
| 3 | 再加大厅中的 `/start` |
| 4 | 再加 `/kick` 踢出玩家 |
| 5 | 再加 `/ban` 封禁玩家（必须提供原因） |

权限等级逐级累加。`/me` 不带编号时可查看自己的信息；`/me <编号>` 查看其他玩家信息需要等级 2。`/warn`、`/kick`、`/ban` 仍受目标保护和房主限制约束，不能对任意目标执行；`/fix` 也仍受其目标有效性检查限制。房主权限、内置开发者权限和允许所有玩家改名／改色的游戏选项仍独立生效。

`SayCommandAccess`、`EndCommandAccess`、`ExecuteCommandAccess` 分别独立授权 `/say`、`/end`、`/exe`；`AssignGameMaster` 只指定 GM 身份，**不会**自动授予管理命令。GM 身份和权限等级互不替代。这些字段接受 `true`、`yes`、`y`、`t`、`1`，其余值视为关闭；生成器使用的 `y` 和 `n` 也都能识别。本地文件授予的玩家权限只从单玩家 TXT 读取，标签文字或 `tags.txt` 都不能授予权限。

单玩家 TXT 可放在 `Tags` 根目录或任意直接子目录（一层），例如 `Tags/moderators/<好友码>.txt`、`Tags/vips/<好友码>.txt`、`Tags/MOD_TAGS/<好友码>.txt`、`Tags/VIP_TAGS/<好友码>.txt` 或 `Tags/SPONSOR_TAGS/<好友码>.txt`。目录名只用于整理文件，不决定玩家标签、身份或权限；这些内容完全由文件字段决定。有效结构化配置至少要包含一个可识别字段：`TagName`、`TagColor`、`PermissionLevel`、`SayCommandAccess`、`EndCommandAccess`、`ExecuteCommandAccess` 或 `AssignGameMaster`。`MOD_TAGS` 和 `VIP_TAGS` 中旧的纯颜色文件不是结构化玩家配置，不会创建 MOD/VIP 身份或授予权限，也不会挡住同一好友码的其他有效配置。若同一好友码在多个位置有有效文件，所有直接子目录都先于根目录；多个子目录按目录名字母顺序检查，先匹配的文件优先。匹配按完整好友码进行。`SPONSOR_TAGS` 中只有恰好为六位十六进制颜色的纯色文件会作为 DevUser 内置标签颜色覆盖，不会被当作结构化玩家配置或给普通玩家添加赞助身份。

单个配置文件超过 16 KiB 时不会读取。

## DevUser 内置标签颜色覆盖

`SPONSOR_TAGS` 中的纯色文件可覆盖 DevUser 的模组内置标签颜色；这种旧式颜色文件不能创建标签、添加身份或授予权限。该目录也可像其他直接子目录一样存放结构化的 `<好友码>.txt` 玩家配置，目录名不会决定权限或身份。若目录不存在，可以手动创建；以下路径相对于房主的游戏工作目录。

| 颜色文件 | 生效条件 | 默认颜色 |
| --- | --- | --- |
| `TOHEE-DATA/Tags/SPONSOR_TAGS/<DevUser好友码>.txt`（内容只有六位颜色） | DevUser 具有内置彩色标签，且使用模组客户端 | DevUser 内置标签的颜色 |

例如，创建 `TOHEE-DATA/Tags/SPONSOR_TAGS/<DevUser好友码>.txt`，文件内容只写：

```text
33CCFF
```

这种颜色文件的全部内容只能是六位 RGB 十六进制颜色，不带 `#`，不支持渐变；它不会被解释为玩家配置，也不会让普通玩家变成赞助者。要为任何玩家配置标签或权限，请使用包含 `TagName:`、`TagColor:`、`PermissionLevel:` 等字段的结构化文件；放在 `SPONSOR_TAGS` 时也由这些字段决定效果。

修改 `SPONSOR_TAGS` 颜色文件后，在大厅执行 `/tags reload` 刷新名字，也可启用下述自动重载选项或重新开房。模组内置开发者标签（包括 DevUser）仍按模组内置行为显示。

## 在游戏中添加或删除

房主输入：

```text
/addtag <玩家编号> <六位十六进制颜色> <标签文字>
/deletetag <玩家编号>
```

例如，`/addtag 3 33CCFF 测试标签` 会给编号为 3 的玩家添加浅蓝色**后备标签**。`/createtag` 与 `/addtag` 相同。添加命令在游戏和大厅都能使用；名字中的标签在大厅显示。删除命令只能在大厅使用。玩家必须仍连接在当前房间，且有好友码。

颜色写成六位 RGB 十六进制，不带 `#`，例如 `FF00AA`。颜色或参数无效时，命令会提示用法，不会保存标签。对同一玩家再次使用 `/addtag` 会替换 `tags.txt` 中的旧标签。`/deletetag` 只删除 `tags.txt` 的条目，不会修改单玩家 TXT 文件；若该文件有有效 `TagName`，它会继续优先显示。

## 手动编辑后备标签文件

首次成功添加标签后，游戏工作目录中会出现 `TOHEE-DATA/tags.txt`。这是房主设备上的文件，不会上传给其他房主。每行按“好友码=带颜色的标签”保存，例如：

```text
sampleuser#1234=<color=#33CCFFFF>测试标签 </color>
otheruser#5678=<color=#FF00AAFF>活动主持人 </color>
```

等号只在好友码后面的第一个位置充当分隔符，因此颜色标记和标签文字中的 `=` 可以正常读取。手工编辑后，房主输入 `/tags reload` 即可重载文件；在大厅执行时还会立即刷新显示，重新开房也会重新加载。删除文件中的某行并重载，会移除该玩家的后备标签；单玩家 TXT 中的标签不受影响。文件不存在时不会自动给任何人加后备标签。

房主也可以在 Client Options 中开启“自动重载标签”。启用后每 5 秒检查 `TOHEE-DATA/Tags` 根目录及直接子目录中的 `.txt` 文件，以及后备文件 `TOHEE-DATA/tags.txt`；发现文件增删、重命名、大小或修改时间变化时，自动重载标签，并在大厅刷新玩家名字。`/tags reload` 仍可随时手动使用。该选项默认关闭。

`LocalPlayerTags.json` 已停用，里面的数据不会迁移。旧的 Moderator/VIP 名单及颜色目录也不会自动迁移，旧文件不会被自动删除。本地文件中的权限和 GM 身份只从单玩家 TXT 读取；`tags.txt` 仍是外观后备，不会授予权限。

单玩家 TXT 标签优先于 `tags.txt` 外观后备标签。模组内置开发者标签仍可与本地标签一起显示在前面；内置赞助标签颜色可由 `SPONSOR_TAGS` 覆盖。如果启用了“隐藏所有标签和文字”，本地标签也会隐藏。

## 与 EHR 的关系

`tags.txt` 的命令形式和“好友码=标签”格式参考了 EHR。本模组按第一个 `=` 分隔，确保 `<color=#...>` 在重开房间后仍能加载。EHR 的私有标签不直接授予 VIP、协管或管理员命令，但有效标签会使玩家被 EHR 判为可信：这会跳过其房主侧 RPC 限速，并在部分反作弊处置模式下免于自动踢出或封禁；检测本身仍可能拦截或报告异常，并不代表豁免官方反作弊。TOHEE 的 `tags.txt` 不授予这些信任效果。参见 EHR 的[可信判断](https://github.com/Gurge44/EndlessHostRoles/blob/6ced0ff0880a4efd654c9d0734faf9c37f43ce5b/Modules/ExtendedPlayerControl.cs#L2440-L2455)、[RPC 限速](https://github.com/Gurge44/EndlessHostRoles/blob/6ced0ff0880a4efd654c9d0734faf9c37f43ce5b/Modules/RPC.cs#L294-L306)和[反作弊处置](https://github.com/Gurge44/EndlessHostRoles/blob/6ced0ff0880a4efd654c9d0734faf9c37f43ce5b/Modules/EAC.cs#L1059-L1109)。TOHEE 从当前连接的玩家所有者读取好友码，以免过期的玩家对象匹配到错误标签。
