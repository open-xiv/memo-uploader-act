using System;


namespace MemoUploader.Helpers;

internal static class MapHelper
{
    public static string ServerEnToZh(string serverEn)
        => serverEn.ToLower() switch
        {
            "luxingniao" => "陆行鸟",
            "moguli" => "莫古力",
            "maoxiaopang" => "猫小胖",
            "doudouchai" => "豆豆柴",
            "hongyuhai" => "红玉海",
            "shenyizhidi" => "神意之地",
            "lanuoxiya" => "拉诺西亚",
            "huanyingqundao" => "幻影群岛",
            "mengyachi" => "萌芽池",
            "yuzhouheyin" => "宇宙和音",
            "woxianxiran" => "沃仙曦染",
            "chenxiwangzuo" => "晨曦王座",
            "baiyinxiang" => "白银乡",
            "baijinhuanxiang" => "白金幻象",
            "shenquanhen" => "神拳痕",
            "chaofengting" => "潮风亭",
            "lvrenzhanqiao" => "旅人栈桥",
            "fuxiaozhijian" => "拂晓之间",
            "longchaoshendian" => "龙巢神殿",
            "mengyubaojing" => "梦羽宝境",
            "zishuizhanqiao" => "紫水栈桥",
            "yanxia" => "延夏",
            "jingyuzhuangyuan" => "静语庄园",
            "moduna" => "摩杜纳",
            "haimaochawu" => "海猫茶屋",
            "roufenghaiwan" => "柔风海湾",
            "hupoyuan" => "琥珀原",
            "shuijingta" or "shuijingta2" => "水晶塔",
            "yinleihu" or "yinleihu2" => "银泪湖",
            "taiyanghaian" or "taiyanghaian2" => "太阳海岸",
            "yixiujiade" or "yixiujiade2" => "伊修加德",
            "hongchachuan" or "hongchachuan2" => "红茶川",
            _ => serverEn
        };

    public static DateTime TimeToUtc(DateTime dt)
        => dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime()
        };
}
