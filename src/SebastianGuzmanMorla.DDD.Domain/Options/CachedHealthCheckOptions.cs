namespace SebastianGuzmanMorla.DDD.Domain.Options;

public class CachedHealthCheckOptions
{
    public const string Section = "CachedHealthCheck";
    
    public string RedisKey { get; set; } = "Ddd:health";
    public string RedisLockKey { get; set; } = "Ddd:locks:health";
    public int CacheIntervalSeconds { get; set; } = 60;
}
