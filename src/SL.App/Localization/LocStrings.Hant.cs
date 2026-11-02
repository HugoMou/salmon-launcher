// SalmonLauncher (SL) — a Minecraft Java edition launcher
// Copyright (C) 2026 Hugo Mou
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
// PARTICULAR PURPOSE. See the GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with
// this program. If not, see <https://www.gnu.org/licenses/>.
//
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace SL.App.Localization;

/// <summary>繁體中文:按词替换(未收录的保持简体,繁体读者可读)。</summary>
public static partial class Loc
{
    private static readonly (string Source, string Target)[] HantWords =
    [
        ("软件", "軟體"), ("启动", "啟動"), ("设置", "設定"), ("账户", "帳戶"), ("外观", "外觀"),
        ("主题", "主題"), ("配色", "配色"), ("强调", "強調"), ("下载", "下載"), ("安装", "安裝"),
        ("实例", "實例"), ("模组", "模組"), ("存档", "存檔"), ("日志", "日誌"), ("诊断", "診斷"),
        ("联机", "連線"), ("皮肤", "皮膚"), ("协议", "協議"), ("隐私", "隱私"), ("免责", "免責"),
        ("文件", "檔案"), ("目录", "目錄"), ("数据", "資料"), ("内存", "記憶體"), ("窗口", "視窗"),
        ("网络", "網路"), ("地址", "位址"), ("端口", "連接埠"), ("服务", "服務"), ("读取", "讀取"),
        ("检查", "檢查"), ("校验", "校驗"), ("镜像", "鏡像"), ("并发", "並行"), ("过滤", "過濾"),
        ("关键词", "關鍵字"), ("错误", "錯誤"), ("警告", "警告"), ("信息", "資訊"), ("复制", "複製"),
        ("粘贴", "貼上"), ("确定", "確定"), ("取消", "取消"), ("删除", "刪除"), ("导入", "匯入"),
        ("导出", "匯出"), ("选择", "選擇"), ("返回", "返回"), ("继续", "繼續"), ("暂停", "暫停"),
        ("重试", "重試"), ("刷新", "重新整理"), ("恢复", "恢復"), ("默认", "預設"), ("跟随", "跟隨"),
        ("系统", "系統"), ("浅色", "淺色"), ("深色", "深色"), ("中文", "中文"), ("语言", "語言"),
        ("繁体", "繁體"), ("简体", "簡體"), ("游戏", "遊戲"), ("运行", "執行"), ("环境", "環境"),
        ("状态", "狀態"), ("进度", "進度"), ("任务", "任務"), ("中心", "中心"), ("崩溃", "崩潰"),
        ("报告", "報告"), ("结论", "結論"), ("建议", "建議"), ("截图", "截圖"), ("壁纸", "壁紙"),
        ("图片", "圖片"), ("方块", "方塊"), ("纹理", "材質"), ("取色", "取色"), ("生成", "產生"),
        ("应用", "套用"), ("预览", "預覽"), ("拖动", "拖曳"), ("旋转", "旋轉"), ("重置", "重設"),
        ("视角", "視角"), ("本地", "本機"), ("网址", "網址"), ("添加", "新增"), ("删除该", "刪除該"),
        ("备份", "備份"), ("占用", "佔用"), ("大小", "大小"), ("用户", "使用者"), ("管理", "管理"),
        ("皮肤站", "皮膚站"), ("主题工坊", "主題工坊"), ("关于", "關於"), ("条款", "條款"),
        ("同意", "同意"), ("并", "並"), ("与", "與"), ("为", "為"), ("这个", "這個"), ("这些", "這些"),
        ("请", "請"), ("说明", "說明"), ("支持", "支援"), ("可以", "可以"), ("没有", "沒有"),
        ("存在", "存在"), ("失败", "失敗"), ("成功", "成功"), ("原因", "原因"), ("解决", "解決"),
        ("帮助", "說明"), ("如果", "如果"), ("因为", "因為"), ("所以", "所以"), ("然后", "然後"),
        ("连接", "連線"), ("断开", "中斷"), ("房间", "房間"), ("邀请", "邀請"), ("成员", "成員"),
        ("发送", "傳送"), ("消息", "訊息"), ("输入", "輸入"), ("搜索", "搜尋"), ("版本", "版本"),
        ("快照", "快照"), ("远古", "遠古"), ("稳定", "穩定"), ("加载", "載入"), ("器", "器"),
        ("内存分配", "記憶體分配"), ("窗口尺寸", "視窗大小"), ("附加", "附加"), ("参数", "參數"),
        ("像素", "像素"), ("同时", "同時"), ("进行的", "進行的"), ("数", "數"), ("全部", "全部"),
        ("清除", "清除"), ("完成", "完成"), ("正在", "正在"), ("打开", "開啟"), ("关闭", "關閉"),
        ("等待", "等待"), ("异步", "非同步"), ("日志目录", "日誌目錄"), ("数据目录", "資料目錄"),
    ];

    private static string ToHant(string source)
    {
        if (string.IsNullOrEmpty(source))
            return source;

        var text = source;
        foreach (var (from, to) in HantWords)
        {
            if (text.Contains(from, StringComparison.Ordinal))
                text = text.Replace(from, to, StringComparison.Ordinal);
        }
        return text;
    }

    /// <summary>繁体还原(用于切回其他语言时复原原文)。</summary>
    private static string FromHant(string source)
    {
        if (string.IsNullOrEmpty(source))
            return source;

        var text = source;
        for (var i = HantWords.Length - 1; i >= 0; i--)
        {
            var (from, to) = HantWords[i];
            if (text.Contains(to, StringComparison.Ordinal))
                text = text.Replace(to, from, StringComparison.Ordinal);
        }
        return text;
    }
}
