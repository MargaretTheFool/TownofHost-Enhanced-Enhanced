using System.IO;

namespace TOHE;

public class DevUser(string code = "", string color = "null", string userType = "null", string tag = "null", bool isUp = true, bool isDev = false, bool deBug = false, bool colorCmd = false, bool nameCmd = false, string upName = "UnKnown")
{
    public string Code { get; set; } = code;
    public string Color { get; set; } = color;
    public string UserType { get; set; } = userType;
    public string Tag { get; set; } = tag;
    public bool IsUp { get; set; } = isUp;
    public bool IsDev { get; set; } = isDev;
    public bool DeBug { get; set; } = deBug;
    public bool ColorCmd { get; set; } = colorCmd;
    public bool NameCmd { get; set; } = nameCmd;
    public string UpName { get; set; } = upName;

    public bool HasTag() => Tag != "null";
    //public string GetTag() => Color == "null" ? $"<size=1.2>{Tag}</size>\r\n" : $"<color={Color}><size=1.2>{(Tag == "#Dev" ? Translator.GetString("Developer") : Tag)}</size></color>\r\n";
    public string GetTag() => GetTag(true);
    public string GetTag(bool useLocalColor)
    {
        string tagColorFilePath = Path.Combine(TOHE.Modules.DataDirectoryMigration.DataDirectory, "Tags", "SPONSOR_TAGS", $"{Code}.txt");

        if (Color == "null" || Color == string.Empty) return $"<size=1.2>{Tag}</size>\r\n";
        var startColor = Color.TrimStart('#');

        if (useLocalColor && File.Exists(tagColorFilePath))
        {
            var ColorCode = File.ReadAllText(tagColorFilePath).Trim();
            if (Utils.CheckColorHex(ColorCode)) startColor = ColorCode;
        }
        string t1;
        t1 = Tag == "#Dev" ? Translator.GetString("Developer") : Tag;
        return $"<size=1.2><color=#{startColor}>{t1}</color></size>\r\r\n";
    }
    //public string GetTag() 
    //{
    //    string tagColorFilePath = @$"./TOHE-DATA/Tags/SPONSOR_TAGS/{Code}.txt";

    //    if (Color == "null" || Color == string.Empty) return $"<size=1.2>{Tag}</size>\r\n";
    //    var startColor = "FFFF00";
    //    var endColor = "FFFF00";
    //    var startColor1 = startColor;
    //    var endColor1 = endColor;
    //    if (Color.Split(",").Length == 1)
    //    {
    //        startColor1 = Color.Split(",")[0].TrimStart('#');
    //        endColor1 = startColor1;
    //    }
    //    else if (Color.Split(",").Length == 2)
    //    {
    //         startColor1 = Color.Split(",")[0].TrimStart('#');
    //         endColor1 = Color.Split(",")[1].TrimStart('#');
    //    }
    //    if (File.Exists(tagColorFilePath))
    //    {
    //        var ColorCode = File.ReadAllText(tagColorFilePath);
    //        if (ColorCode.Split(" ").Length == 2)
    //        {
    //            startColor = ColorCode.Split(" ")[0];
    //            endColor = ColorCode.Split(" ")[1];
    //        }
    //        else
    //        {
    //            startColor = startColor1;
    //            endColor = endColor1;
    //        }
    //    }
    //    else
    //    {
    //        startColor = startColor1;
    //        endColor = endColor1;
    //    }
    //    if (!Utils.CheckGradientCode($"{startColor} {endColor}"))
    //    {
    //        startColor = "FFFF00";
    //        endColor = "FFFF00";
    //    }
    //    var t1 = "";
    //    t1 = Tag == "#Dev" ? Translator.GetString("Developer") : Tag;
    //    return $"<size=1.2>{Utils.GradientColorText(startColor,endColor, t1)}</size>\r\n";
    //}
}

public static class DevManager
{
    private readonly static DevUser DefaultDevUser = new();
    public readonly static List<DevUser> DevUserList = [];
    public static void Init()
    {
        // Dev
        DevUserList.Add(new(code: "actorour#0029", color: "#ffc0cb", tag: "Original Developer", userType: "s_cr", isUp: true, isDev: true, deBug: true, colorCmd: true, upName: "KARPED1EM"));
        DevUserList.Add(new(code: "pinklaze#1776", color: "#30548e", tag: "#Dev", userType: "s_cr", isUp: true, isDev: true, deBug: true, colorCmd: false, upName: "NCSIMON"));
        DevUserList.Add(new(code: "sofaagile#3120", color: "null", tag: "null", userType: "s_cr", isUp: true, isDev: true, deBug: true, colorCmd: false, upName: null)); //天寸
        //DevUserList.Add(new(code: "keyscreech#2151", color: "null", tag: "<color=#D3A4FF>美術</color><color=#5A5AAD>NotKomi</color>", isUp: false, isDev: true, deBug: false, upName: null)); //Endrmen40409
        DevUserList.Add(new(code: "icingposh#6469", color: "#9e2424", userType: "s_cr", tag: "ryuk2", isUp: true, isDev: true, deBug: true, colorCmd: true, upName: "ryuk2"));
        DevUserList.Add(new(code: "bestanswer#3360", color: "#00ff1d", tag: "绿色游戏", userType: "s_cr", isUp: true, isDev: true, deBug: true, colorCmd: true, upName: null)); //NikoCat233's alt
        DevUserList.Add(new(code: "happypride#3747", color: "#00ff1d", tag: "绿色游戏", userType: "s_cr", isUp: true, isDev: true, deBug: true, colorCmd: true, upName: null)); //NikoCat233's alt 2
        //// pt-BR Translators
        //DevUserList.Add(new(code: "modelpad#5195", color: "null", tag: "Tradutor", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "Reginaldoo")); // and content creator
        //DevUserList.Add(new(code: "mimerecord#9638", color: "null", tag: "Tradutor", isUp: false, isDev: false, deBug: false, colorCmd: false, upName: "Arc"));

        //// Latam Translators
        //DevUserList.Add(new(code: "magicyear#5568", color: "#1F75FE", tag: "Traductor", isUp: false, isDev: false, deBug: false, colorCmd: false, upName: "CreepPower")); //creeppower

        //// Chinese Content makers
        DevUserList.Add(new(code: "truantwarm#9165", color: "null", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "萧暮不姓萧"));
        DevUserList.Add(new(code: "drilldinky#1386", color: "null", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "爱玩AU的河豚"));
        DevUserList.Add(new(code: "farardour#6818", color: "null", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "-提米SaMa-"));
        DevUserList.Add(new(code: "vealused#8192", color: "null", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "lag丶xy"));
        DevUserList.Add(new(code: "storyeager#0815", color: "null", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "航娜丽莎"));
        DevUserList.Add(new(code: "versegame#3885", color: "null", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "柴唔cw"));
        DevUserList.Add(new(code: "closegrub#6217", color: "null", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "警长不会玩"));
        DevUserList.Add(new(code: "frownnatty#7935", color: "null", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "鬼灵official"));
        DevUserList.Add(new(code: "veryscarf#5368", color: "null", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "小武同学102"));
        DevUserList.Add(new(code: "sparklybee#0275", color: "null", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "--红包SaMa--"));
        DevUserList.Add(new(code: "endingyon#3175", color: "null", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "游侠开摆"));
        DevUserList.Add(new(code: "firmine#0232", color: "null", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "YH永恒_"));
        DevUserList.Add(new(code: "fellowsand#1003", color: "null", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "C-Faust"));
        DevUserList.Add(new(code: "jetsafe#8512", color: "null", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "Hoream是好人"));
        DevUserList.Add(new(code: "spoonkey#0792", color: "null", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "没好康的"));
        DevUserList.Add(new(code: "busethical#4134", color: "null", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "茄-au"));
        DevUserList.Add(new(code: "marlymoor#2246", color: "null", tag: "null", isUp: true, isDev: false, colorCmd: false, deBug: false, upName: "落Yan"));

        DevUserList.Add(new(code: "neatnet#5851", color: "#FFFF00", tag: "The 200IQ guy", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "The 200IQ guy"));
        DevUserList.Add(new(code: "heavyclod#2286", color: "#FFFF00", tag: "小叨.exe已停止运行", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "小叨院长"));
        DevUserList.Add(new(code: "storeroan#0331", color: "#FF0066", tag: "Night_瓜", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "Night_瓜"));
        DevUserList.Add(new(code: "teamelder#5856", color: "#1379bf", tag: "屑Slok（没信誉的鸽子）", isUp: true, isDev: false, colorCmd: false, deBug: false, upName: "Slok7565"));
        DevUserList.Add(new(code: "freepit#9942", color: "#00FFFF", tag: "古明地白糖", isUp: true, isDev: false, colorCmd: false, deBug: false, upName: "古明地白糖"));
        DevUserList.Add(new(code: "radarright#2509", color: "null", tag: "null", isUp: true, isDev: false, deBug: true, colorCmd: false, upName: null));

        //// Sponsor
        DevUserList.Add(new(code: "recentduct#6068", color: "#FF00FF", tag: "高冷男模法师", isUp: true, isDev: false, colorCmd: false, deBug: true, upName: null));
        DevUserList.Add(new(code: "canneddrum#2370", color: "#fffcbe", tag: "我是喜唉awa", isUp: true, isDev: false, colorCmd: false, deBug: false, upName: null));
        DevUserList.Add(new(code: "dovefitted#5329", color: "#1379bf", tag: "不要首刀我", isUp: true, isDev: false, colorCmd: false, deBug: false, upName: null));
        DevUserList.Add(new(code: "luckylogo#7352", color: "#f30000", tag: "林@林", isUp: true, isDev: false, colorCmd: false, deBug: false, upName: null));
        DevUserList.Add(new(code: "axefitful#8788", color: "#8e8171", tag: "寄才是真理", isUp: true, isDev: false, colorCmd: false, deBug: false, upName: null));
        DevUserList.Add(new(code: "raftzonal#8893", color: "#8e8171", tag: "寄才是真理", isUp: true, isDev: false, colorCmd: false, deBug: false, upName: null));
        DevUserList.Add(new(code: "twainrobin#8089", color: "#0000FF", tag: "啊哈修maker", isUp: true, isDev: false, colorCmd: false, deBug: false, upName: null));
        DevUserList.Add(new(code: "mallcasual#6075", color: "#f89ccb", tag: "波奇酱", isUp: true, isDev: false, colorCmd: false, deBug: false, upName: null));
        DevUserList.Add(new(code: "beamelfin#9478", color: "#6495ED", tag: "Amaster-1111", isUp: true, isDev: false, colorCmd: false, deBug: false, upName: null));
        DevUserList.Add(new(code: "lordcosy#8966", color: "#FFD6EC", tag: "HostTOHE", isUp: true, isDev: false, colorCmd: false, deBug: false, upName: null)); //K
        DevUserList.Add(new(code: "honestsofa#2870", color: "#D381D9", tag: "SolarFlare", isUp: true, isDev: false, colorCmd: false, deBug: false, upName: "SolarFlare")); //SolarFlare

        // Imported from userinfo_f.json
        DevUserList.Add(new(code: "doggedsize#7892", color: "null", userType: "s_tr", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "tronandrey"));
        DevUserList.Add(new(code: "motorlace#4741", color: "#DFB722", userType: "s_jc", tag: "null", isUp: true, isDev: false, deBug: true, colorCmd: false, upName: "sussynba"));
        DevUserList.Add(new(code: "thenfrozen#1719", color: "#FF0000", userType: "s_ms", tag: "Sponsor", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "misstoh"));
        DevUserList.Add(new(code: "fivedogged#3140", color: "#00FF00", userType: "t_fr", tag: "Translator", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "cloakhazy#9133", color: "#87CEFA", userType: "s_cr", tag: "我是崽子吖awa", isUp: true, isDev: true, deBug: true, colorCmd: true, upName: "lezaiya"));
        DevUserList.Add(new(code: "fluffycord#2605", color: "#880000", userType: "s_gd", tag: "discord,gg/maul", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "sarhadactyl"));
        DevUserList.Add(new(code: "iconicdrop#2727", color: "#FF0000", userType: "s_tr", tag: "You<color=#ffffff>Tuber</color>", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "jackler1"));
        DevUserList.Add(new(code: "funnelshy#3354", color: "#0000FF", userType: "s_te", tag: "SNOW RABBIT", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "shirousagi1373"));
        DevUserList.Add(new(code: "nikocat#9999", color: "#00FF1D", userType: "s_cr", tag: "绿色游戏", isUp: true, isDev: true, deBug: true, colorCmd: true, upName: "nikocat233"));
        DevUserList.Add(new(code: "drawncod#3642", color: "#00FFFF", userType: "t_cs", tag: "简中翻译人员", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "microsand#3017", color: "#ff0000", userType: "s_in", tag: "Sponsor", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "beechary#2537", color: "#009900", userType: "t_it", tag: "Translator", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "latevoice#4590", color: "#FF0000", userType: "s_jc", tag: "Sponsor", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "notedcomb#6150", color: "#ff0000", userType: "s_sp", tag: "Sponsor", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "zacian033378"));
        DevUserList.Add(new(code: "mixedpoet#0041", color: "#0C659E", userType: "s_sp", tag: "Translator", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "ahssidic"));
        DevUserList.Add(new(code: "richgaff#1771", color: "#f77221", userType: "t_ct", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "fawnprize#5981", color: "null", userType: "s_sp", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "nicolejm88"));
        DevUserList.Add(new(code: "mindproof#2438", color: "null", userType: "s_tr", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "sparky15756"));
        DevUserList.Add(new(code: "risingbean#8769", color: "#FF0040", userType: "t_nl", tag: "Translator", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "bigecho#5256", color: "#0131B4", userType: "t_fr", tag: "Traducteur", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "boldticker#8708", color: "#ff0000", userType: "s_sp", tag: "Sponsor", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "therealoliverqueen"));
        DevUserList.Add(new(code: "fluidfree#0232", color: "#ffc0cb", userType: "s_bo", tag: "Booster", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "openlanded#9533", color: "#9E2424", userType: "s_cr", tag: "God of Death Love Apples", isUp: true, isDev: true, deBug: true, colorCmd: true, upName: "ryuk10"));
        DevUserList.Add(new(code: "motelchief#4112", color: "#e890b2", userType: "s_cr", tag: "Contributor", isUp: true, isDev: true, deBug: true, colorCmd: true, upName: "drakos_yeager"));
        DevUserList.Add(new(code: "archkelp#9155", color: "null", userType: "s_tr", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "floxiid"));
        DevUserList.Add(new(code: "singlesign#1823", color: "#FFB6CD", userType: "s_tr", tag: "Head of HR", isUp: true, isDev: true, deBug: false, colorCmd: true, upName: "itslaurynnicole"));
        DevUserList.Add(new(code: "wellsup#8385", color: "null", userType: "s_cr", tag: "Contributor", isUp: true, isDev: true, deBug: true, colorCmd: true, upName: "margaretthefool"));
        DevUserList.Add(new(code: "innerruler#4140", color: "#4DD198", userType: "t_es", tag: "Traductor", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "monthlate#1438", color: "#80582D", userType: "t_fr", tag: "Traducteur", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "contenthue#0404", color: "#FFFF00", userType: "s_cr", tag: "The 200IQ guy", isUp: true, isDev: true, deBug: true, colorCmd: true, upName: "gurge44"));
        DevUserList.Add(new(code: "untoldcode#5639", color: "#ff0000", userType: "s_sp", tag: "Sponsor", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "hyperelliott"));
        DevUserList.Add(new(code: "swiftlord#8072", color: "#E64236", userType: "s_tr", tag: "Translator", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "butwhatabout"));
        DevUserList.Add(new(code: "fastgecko#5461", color: "#00FF00", userType: "t_it", tag: "Translator", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "freesolid#2882", color: "#FFC0CB", userType: "s_tr", tag: "Booster", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "ftpeacock"));
        DevUserList.Add(new(code: "keepchirpy#6354", color: "#1FF3C6", userType: "s_cr", tag: "Переводчик", isUp: true, isDev: true, deBug: true, colorCmd: true, upName: "tommyxl"));
        DevUserList.Add(new(code: "rarespite#3723", color: "#ff0000", userType: "s_sp", tag: "Sponsor", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "buddah2400"));
        DevUserList.Add(new(code: "wristterse#0403", color: "#ff0000", userType: "s_ms", tag: "Sponsor", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "ish1997"));
        DevUserList.Add(new(code: "primether#5348", color: "#FF0000", userType: "s_tr", tag: "YouTuber", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "anonworks"));
        DevUserList.Add(new(code: "lordhefty#7765", color: "#FF0000", userType: "s_tr", tag: "YouTuber", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "nickmasterxx"));
        DevUserList.Add(new(code: "timedapper#9496", color: "#48FFFF", userType: "t_ct", tag: "繁體中文翻譯", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "smokyspawn#9072", color: "#ff0000", userType: "s_sp", tag: "Sponsor", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "firmonce#5157", color: "#ff0000", userType: "s_in", tag: "Sponsor", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "fireretro#9325", color: "#00FF00", userType: "s_cr", tag: "Contributor", isUp: true, isDev: true, deBug: true, colorCmd: true, upName: "pietro420"));
        DevUserList.Add(new(code: "lordwest#8641", color: "#ff0000", userType: "s_in", tag: "Sponsor", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "primebulb#9031", color: "#ff0000", userType: "s_ms", tag: "Sponsor", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "cultanxi"));
        DevUserList.Add(new(code: "avidloser#2507", color: "#ccccff", userType: "s_sp", tag: "Sponsor", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "goth.pet"));
        DevUserList.Add(new(code: "tinedpun#6584", color: "#0000FF", userType: "t_br", tag: "Translator", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "straymovie#6453", color: "#ff8000", userType: "s_cr", tag: "President/CEO", isUp: true, isDev: true, deBug: true, colorCmd: true, upName: "moe.dev"));
        DevUserList.Add(new(code: "notegusty#8646", color: "#4bf4ff", userType: "s_cr", tag: "TEN Lead Developer", isUp: true, isDev: true, deBug: true, colorCmd: true, upName: "d1gq"));
        DevUserList.Add(new(code: "merryshow#9985", color: "null", userType: "s_tr", tag: "null", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "primsodium"));
        DevUserList.Add(new(code: "cannylink#0564", color: "#FF0000", userType: "s_ms", tag: "Sponsor", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: "vmerp"));
        DevUserList.Add(new(code: "anodynesea#2774", color: "null", userType: "s_cr", tag: "null", isUp: true, isDev: true, deBug: true, colorCmd: true, upName: "impostor4291"));
        DevUserList.Add(new(code: "globalwish#5585", color: "#FFFF37", userType: "t_cs", tag: "Translator", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "alphacook#6624", color: "#6B89FF", userType: "t_cs", tag: "简体中文译者", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "eastbutton#6692", color: "#27E878", userType: "t_ct", tag: "繁體中文翻譯", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
        DevUserList.Add(new(code: "laterally#0590", color: "#49E6AC", userType: "s_cr", tag: "Website Dude", isUp: true, isDev: true, deBug: true, colorCmd: true, upName: ".pyro0"));
        DevUserList.Add(new(code: "taskunsold#2701", color: "null", userType: "t_ru", tag: "Translator", isUp: true, isDev: false, deBug: false, colorCmd: false, upName: null));
    }
    public static bool IsDevUser(this string code) => DevUserList.Any(x => x.Code == code);
    internal static DevUser GetBuiltInDevUser(string code) => DevUserList.Find(x => x.Code == code) ?? DefaultDevUser;
    public static DevUser GetDevUser(this string code) => Modules.CloudTags.GetEffective(code) ?? GetBuiltInDevUser(code);
    public static string GetUserType(this DevUser user)
    {
        string rolename = "Crewmate";

        if (user.UserType != "null" && user.UserType != string.Empty)
        {
            switch (user.UserType)
            {
                case "s_cr":
                    rolename = "<color=#ff0000>Contributor</color>";
                    break;
                case "s_bo":
                    rolename = "<color=#7f00ff>Booster</color>";
                    break;
                case "s_tr":
                    rolename = "<color=#f46e6e>Tester</color>";
                    break;
                case "s_jc":
                    rolename = "<color=#f46e6e>Junior Contributor</color>";
                    break;

                default:
                    if (user.UserType.StartsWith("s_"))
                    {
                        rolename = "<color=#ffff00>Sponsor</color>";
                    }
                    else if (user.UserType.StartsWith("t_"))
                    {
                        rolename = "<color=#00ffff>Translator</color>";
                    }
                    break;
            }
        }

        return rolename;
    }

}
