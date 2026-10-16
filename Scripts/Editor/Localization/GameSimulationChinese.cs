using System.Collections.Generic;

namespace VRCLearn.GestureManagerPro.Localization
{
    internal static class GameSimulationChinese
    {
        internal static IReadOnlyDictionary<string, string> Entries { get; } = new Dictionary<string, string>
        {
            ["Gesture Manager Pro"] = "Gesture Manager Pro",
            ["Game Simulation"] = "游戏模拟",
            ["Control the avatar in Game or Scene view.\nKeyboard movement and mouse look."] = "在游戏或场景视图中控制角色。\n使用键盘移动、鼠标转动视角。",
            ["Gravity / Jump"] = "重力 / 跳跃",
            ["Third-Person Distance"] = "第三人称距离",
            ["Controls"] = "操作说明",
            ["Enter Play Mode with an initialized Pro avatar, then enter Game Simulation. WASD controls the avatar in Game or Scene view. Esc releases the Game-view mouse."] = "进入播放模式，等待 Pro 角色初始化后即可进入游戏模拟。WASD 可在游戏或场景视图中控制角色。Esc 释放游戏视图中的鼠标。",
            ["Game view: WASD moves, < / > turns, F5 toggles third-person mode, Shift runs forward / backward, C toggles crouch, and Z toggles prone. While third-person mode is enabled, the wheel adjusts distance; zooming fully in returns to first person. Mouse look and wheel zoom require captured input. Space jumps when gravity is enabled and the avatar is grounded."] = "游戏视图：WASD 移动，< / > 转向，F5 开关第三人称模式，Shift 加速前后移动，C 切换蹲伏，Z 切换俯卧。第三人称模式启用时，滚轮调整距离；拉至最近处会回到第一人称。鼠标视角和滚轮缩放需要先捕获输入。启用重力且角色着地时，Space 跳跃。",
            ["Scene view: WASD moves, < / > turns, Shift runs forward / backward, C toggles crouch, Z toggles prone, and Space jumps when gravity is enabled and the avatar is grounded. Observer following is off by default. Alt, mouse navigation and the wheel remain available to the editor."] = "场景视图：WASD 移动，< / > 转向，Shift 加速前后移动，C 切换蹲伏，Z 切换俯卧。启用重力且角色着地时，Space 跳跃。旁观视角默认不跟随角色。Alt、鼠标导航和滚轮仍由编辑器使用。",
            ["Use Selected / Active Manager"] = "使用选中或正在运行的 GM",
            ["Move Speed (m/s)"] = "移动速度（米/秒）",
            ["Turn Speed (degrees/s)"] = "转向速度（度/秒）",
            ["Scene Collisions"] = "场景碰撞",
            ["Gravity / Jump (requires a ground collider)"] = "重力 / 跳跃（需要地面碰撞体）",
            ["View"] = "视角",
            ["Follow Avatar in Observer View"] = "旁观视角跟随角色",
            ["Move the Scene view and its Game-view camera with the avatar. Rotation and zoom stay under your control."] = "让场景视图及其游戏视图相机随角色移动。旋转与缩放仍由你控制。",
            ["First Person"] = "第一人称",
            ["Third Person"] = "第三人称",
            ["Third-Person Distance (avatar heights; zero returns to first person)"] = "第三人称距离（角色身高倍数；0 返回第一人称）",
            ["Mouse Look Sensitivity"] = "鼠标视角灵敏度",
            ["Esc releases captured Game-view input. Click the Game view or use Resume Input to continue. Select Scene Camera in View for a free observer cursor. First person hides the local avatar from the simulation camera; observer and third-person views still show it. Visible first-person body / arms are not implemented yet. Exit restores the avatar pose, motion parameters, cameras and cursor."] = "Esc 释放已捕获的游戏视图输入。点击游戏视图或使用“继续控制”恢复。在“视角”中选择“场景相机”可使用鼠标自由的旁观视角。第一人称会在模拟相机中隐藏本地角色；旁观视角和第三人称仍会显示角色。目前尚不支持在第一人称下显示身体 / 手臂。退出后恢复角色姿势、运动参数、相机和光标。",
            ["Enter Game Simulation"] = "进入游戏模拟",
            ["Exit and Restore"] = "退出并恢复",
            ["Resume Input"] = "继续控制",
            ["Keyboard input requires Active Input Handling: Input Manager (Old) or Both. The motion input interface is ready for future VR adapters; headset tracking and IK are not implemented yet."] = "键盘输入需要将 Active Input Handling 设为 Input Manager (Old) 或 Both。运动输入接口已预留，供未来接入 VR 设备；尚未实现头显追踪和 IK。",
            ["Game Simulation is stopped. Enter Play Mode to begin."] = "游戏模拟已停止。进入播放模式后可开始。",
            ["Movement is blocked by avatar locomotion or pose settings."] = "角色的移动或姿势设置阻止了移动。",
            ["Input suspended: click the Game view or use Resume Input, or focus the Scene view, then release all control keys."] = "输入已暂停：点击游戏视图或使用“继续控制”，也可将焦点移到场景视图；随后松开所有控制按键。",
            ["Scene input suspended: release control keys, Alt and the mouse buttons."] = "场景视图输入已暂停：请松开控制按键、Alt 和鼠标按键。",
            ["Scene avatar control — speed {0:F2} m/s, turn {1:F1} degrees/s, grounded {2}"] = "场景视图控制 — 速度 {0:F2} 米/秒，转向 {1:F1} 度/秒，着地 {2}",
            ["Game Simulation — speed {0:F2} m/s, turn {1:F1} degrees/s, grounded {2}"] = "游戏模拟 — 速度 {0:F2} 米/秒，转向 {1:F1} 度/秒，着地 {2}",
            ["Yes"] = "是",
            ["No"] = "否",
            ["Enter Play Mode and resume the editor before enabling controlled mode."] = "启用游戏模拟前，请进入播放模式并取消暂停。",
            ["Select an active Gesture Manager Pro controlling an SDK3 avatar."] = "请选择正在控制 SDK3 角色且已启用的 Gesture Manager Pro。",
            ["This manager is already owned by another motion controller."] = "该管理器已由另一个运动控制器接管。",
            ["This avatar is already owned by another motion controller."] = "该角色已由另一个运动控制器接管。",
            ["Another game simulation is already active."] = "另一个游戏模拟已在运行。",
            ["Stop custom animation and dummy modes before enabling controlled mode."] = "启用游戏模拟前，请停止自定义动画和替身模式。",
            ["The simulator has not initialized its motion parameters yet."] = "模拟器尚未初始化运动参数。",
            ["Keyboard control requires Active Input Handling set to Input Manager (Old) or Both."] = "键盘控制需要将 Active Input Handling 设置为 Input Manager (Old) 或 Both。",
        };
    }
}
