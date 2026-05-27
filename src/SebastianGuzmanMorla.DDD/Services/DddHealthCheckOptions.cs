namespace SebastianGuzmanMorla.DDD.Services;

public class DddHealthCheckOptions
{
    public string RedisKey { get; set; } = "Ddd:health";
    public string RedisLockKey { get; set; } = "Ddd:locks:health";
    public int CacheIntervalSeconds { get; set; } = 60;
}
