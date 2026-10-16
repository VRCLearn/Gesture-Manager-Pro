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
            ["Game view: WASD moves, the mouse looks, < / > turns, V switches first / third person, and Shift runs forward / backward. C toggles crouch; Z toggles prone. Space jumps when gravity is enabled and the avatar is grounded."] = "游戏视图：WASD 移动，鼠标转动视角，< / > 转向，V 切换第一 / 第三人称，Shift 加速前后移动。C 切换蹲伏，Z 切换俯卧。启用重力且角色着地时，Space 跳跃。",
            ["Scene view: WASD moves, < / > turns, Shift runs forward / backward, C toggles crouch, Z toggles prone, and Space jumps when gravity is enabled and the avatar is grounded. The Scene camera stays unchanged; Alt or right / middle mouse suspends avatar input for camera navigation."] = "场景视图：WASD 移动，< / > 转向，Shift 加速前后移动，C 切换蹲伏，Z 切换俯卧。启用重力且角色着地时，Space 跳跃。场景相机保持不动；按 Alt 或鼠标右键 / 中键时，暂停角色输入，以便操纵场景相机。",
            ["Use Selected / Active Manager"] = "使用选中或正在运行的 GM",
            ["Move Speed (m/s)"] = "移动速度（米/秒）",
            ["Turn Speed (degrees/s)"] = "转向速度（度/秒）",
            ["Scene Collisions"] = "场景碰撞",
            ["Gravity / Jump (requires a ground collider)"] = "重力 / 跳跃（需要地面碰撞体）",
            ["View"] = "视角",
            ["First Person"] = "第一人称",
            ["Third Person"] = "第三人称",
            ["Third-Person Distance (avatar heights)"] = "第三人称距离（角色身高倍数）",
            ["Mouse Look Sensitivity"] = "鼠标视角灵敏度",
            ["Esc releases the Game-view mouse and suspends input. Click the Game view or use Resume Input to continue. First person hides the local avatar from the simulation camera; the Scene view and third person still show it. Visible first-person body / arms are not implemented yet. Exit restores the avatar pose, motion parameters, cameras and cursor."] = "Esc 释放游戏视图中的鼠标并暂停输入。点击游戏视图或使用“继续控制”恢复。第一人称会在模拟相机中隐藏本地角色；场景视图和第三人称仍会显示角色。目前尚不支持在第一人称下显示身体 / 手臂。退出后恢复角色姿势、运动参数、相机和光标。",
            ["Enter Game Simulation"] = "进入游戏模拟",
            ["Exit and Restore"] = "退出并恢复",
            ["Resume Input"] = "继续控制",
            ["Keyboard input requires Active Input Handling: Input Manager (Old) or Both. The motion input interface is ready for future VR adapters; headset tracking and IK are not implemented yet."] = "键盘输入需要将 Active Input Handling 设为 Input Manager (Old) 或 Both。运动输入接口已预留，供未来接入 VR 设备；尚未实现头显追踪和 IK。",
            ["Game Simulation is stopped. Enter Play Mode to begin."] = "游戏模拟已停止。进入播放模式后可开始。",
            ["Movement is blocked by avatar locomotion or pose settings."] = "角色的移动或姿势设置阻止了移动。",
            ["Input suspended: click the Game view or use Resume Input, or focus the Scene view, then release all control keys."] = "输入已暂停：点击游戏视图或使用“继续控制”，也可将焦点移到场景视图；随后松开所有控制按键。",
            ["Scene input suspended: release control keys, Alt and the mouse buttons."] = "场景视图输入已暂停：请松开控制按键、Alt 和鼠标按键。",
            ["Scene avatar control — speed {0:F2} m/s, turn {1:F1} degrees/s, grounded {2}; Scene camera unchanged"] = "场景视图控制 — 速度 {0:F2} 米/秒，转向 {1:F1} 度/秒，着地 {2}；场景相机保持不动",
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
