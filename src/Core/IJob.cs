namespace SebScheduler.Core
{
    public interface IJob
    {
        bool IsTimeToRun();
    }
}