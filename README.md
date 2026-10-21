# Codex Quota Battery · 额度电池条

**额度看得见，恢复后接着干。** Windows 上的半透明液体电池挂件：左边五小时额度，右边一周额度，还能谨慎地恢复因额度耗尽而中断的 Codex 本地任务。

![液体电池预览](preview-liquid.png)

MIT 开源 / Windows 10、11 / Python 3.10+ / 已安装并登录 Codex Desktop

## 一句话安装

直接把这一段发给你自己的 Codex Desktop：

```text
请帮我安装 https://github.com/1435661587/codex-quota-battery 。先阅读 README 和源码，克隆到独立目录。确认当前是 Windows 且已登录 Codex，用 Python 3.10+ 运行 python install.py --enable-recovery，使用我当前的 Codex 环境，不要复制作者的本机配置或任务ID。安装后打开组件并检查监控状态。只对运行中明确因额度耗尽而失败的任务，在额度恢复后继续；我主动停止的任务不要恢复。不修改模型及审批权限。
```

只想显示额度时，去掉 `--enable-recovery`。新装默认关闭续跑；已有安装保留开关。

手动安装：

```powershell
git clone https://github.com/1435661587/codex-quota-battery.git
cd codex-quota-battery
python install.py --enable-recovery
```

建议在 Codex 中安装，以继承桌面任务连接。安装器用 Windows 自带 .NET Framework 编译器从源码构建，服务仅需 Python 标准库；不下载模型，不需要付费 API Key，不要求管理员权限。

这是第三方开源项目，通过本地 MCP 插件启动自带的 WinForms 小程序，**不是 OpenAI 官方出品，也不是内置宠物接口**。

## 功能

- **双额度电池**：五小时和一周剩余额度，余额越少液面越低、颜色越红。
- **液体玻璃外观**：70×88 像素、立体半透明、连续液面波动，能看见后方文字。
- **拖动与记忆**：置顶、不占任务栏；悬停看百分比及重置时间；右键刷新、开关动画、续跑和退出。
- **快速同步**：优先尝试官方共享通知，无法连接时调用官方只读额度接口，目标每2秒核对。只展示官方已返回的数据，不承诺无延迟。
- **额度恢复续跑**：约每10秒查看本地任务状态，不为监控启动模型对话。
- **排除主动停止**：不恢复 `interrupted`、正常完成、普通报错、新指令覆盖的旧轮次。
- **去重和诊断**：发送前持久化去重状态；右键“查看自动续跑状态”，可见等待和恢复数量。

## 续跑判断

先实际观察到运行中的任务和轮次；最新同一轮必须以 `failed` 结束，并有官方 `usageLimitExceeded` 错误。部分桌面版本省略该字段，此时只匹配 **该终止错误字段** 内官方明确的 `You've hit your usage limit.` 前缀，不匹配对话内容或工具输出中的关键词。

等待至少30秒，并确认五小时及一周官方余额均大于零且数据新鲜；发送前再次检查任务没有重新运行、没有新轮次，才通过 Codex 自带的 `send_message_to_thread` 向原任务发送一次继续指令。不另开 CLI 工作会话，不改变模型、权限或审批，不购买额度。

**使用边界：**

- 仅本机 Codex 任务，不处理 ChatGPT 网页聊天和其他机器。当前桌面接口读取最近50个非置顶任务和全部置顶任务。
- 需电脑、Codex 与挂件持续运行。退出挂件即停止监控，不注册系统常驻服务。
- 无法确认原因就不续跑；未观察到运行过程的旧任务不会补跑。
- 桌面消息接口不支持按旧轮次原子发送，复核能降低但不能完全消除极短时间的操作竞争。
- 发送被拒绝或结果不明确时记录 `needs_attention`，不盲目重发，需用户检查。
- 已验证真实历史额度错误的识别、保护逻辑和本机监控。自然耗尽后的完整无人值守恢复还需更多环境验证，不承诺100%可靠。

右键可关闭“额度恢复后继续中断任务”。关闭会清空待恢复资格；重开只观察之后实际运行的任务。

## 隐私与排查

状态位于 `%LOCALAPPDATA%\CodexQuotaBattery`：`status.json`（额度）、`position.json`（位置）、`recovery-settings.json`（开关）、`recovery-status.json`（状态与数量）、`recovery-state.json`（任务ID、轮次ID、去重及最近100条判断记录）。不保存任务消息正文和项目文件，不上传本地状态或账号凭证。

安装时在本机插件目录生成 `recovery-runtime.json`，其中是当前电脑路径和桌面连接端点；它不属于开源分发内容。接口失败不伪造满额度；当前状态可能是 `monitoring`、`paused`、`connection_error` 或 `needs_attention`。

## 更新与开发

```powershell
git pull --ff-only
python install.py
python release.py check
```

更新只重启本组件，保留位置和开关，不中断 Codex 项目任务。

维护者在 Codex 中修改功能后，按本目录 `AGENTS.md` 完成测试与公开文件检查，再执行：

```powershell
python release.py sync --message "feat: describe the completed change"
```

需 Git 已登录 GitHub。不会在每次保存时偷偷上传未验证的代码；只有完成检查后的明确发布步骤才提交白名单文件并推送。

开源地址：https://github.com/1435661587/codex-quota-battery

接口参考：[OpenAI App Server](https://learn.chatgpt.com/docs/app-server)、[本地插件打包](https://developers.openai.com/plugins/build/plugins)。

MIT 许可，见 LICENSE。Codex/OpenAI 商标归各自权利人所有。
