using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Mvc;
using Nito.AsyncEx;

namespace SpikeAjaxCancellation.Controllers
{
    //[SessionState(SessionStateBehavior.Disabled)]
    public class HomeController : Controller
    {
        private static readonly TimeSpan HeartBeatInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan LongRunningTaskDelay = TimeSpan.FromSeconds(10);
        private readonly AsyncReaderWriterLock _readerWriterLock = new AsyncReaderWriterLock();

        public ActionResult Index()
        {
            return View();
        }

        public async Task<ActionResult> LongRunningActionAsync(int id)
        {
            var start = DateTime.Now;
            int? value;
            using (var readerLock = await _readerWriterLock.UpgradeableReaderLockAsync())
            {
                value = GetCachedValue(id);
                if (value != null)
                    return ContentResult(id, value, start);

                var cancellationTokenSource = new CancellationTokenSource();
                var cancellationToken = cancellationTokenSource.Token;
                var getValueTask = GetValueAsync(id, cancellationToken);
                
                var heartBeat = HeartBeatAsync(
                    HeartBeatInterval,
                    () => Response.IsClientConnected,
                    cancellationToken);
                
                var task = await Task.WhenAny(getValueTask, heartBeat);
                
                cancellationTokenSource.Cancel();
                if (task == heartBeat) return new EmptyResult();

                value = await getValueTask;
                using (var writerLock = await readerLock.UpgradeAsync(cancellationToken))
                {
                    SetCachedValue(id, value);
                }
            }
            return ContentResult(id, value, start);
        }

        private int? GetCachedValue(int id)
        {
            var key = id.ToString(CultureInfo.InvariantCulture);
            return (int?) HttpContext.Cache[key];
        }

        private void SetCachedValue(int id, int? value)
        {
            var key = id.ToString(CultureInfo.InvariantCulture);
            HttpContext.Cache[key] = value;
        }

        private ContentResult ContentResult(int id, object value, DateTime start)
        {
            return Content(string.Format("Id: {0}. Value: {1}. Start: {2}", id, value, start));
        }

        private async Task<int> GetValueAsync(int id, CancellationToken cancellationToken)
        {
            await Task.Delay(LongRunningTaskDelay, cancellationToken);
            return id*10;
        }

        private static async Task HeartBeatAsync(
            TimeSpan heartBeatInterval,
            Func<bool> func,
            CancellationToken cancellationToken)
        {
            while (func())
            {
                await Task.Delay(heartBeatInterval, cancellationToken);
            }
        }
    }
}