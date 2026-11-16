namespace QiuQiu
{
    // 阵营只用来统计（吞噬倍率 / 名次 / 补员计数），不决定谁能吃谁。
    // 谁能吃谁在 Eater 里判，而且只保护玩家自己的球。
    public enum Team
    {
        Player = 0,
        Enemy = 1,
    }
}
