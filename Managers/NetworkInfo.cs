using System.Net.Http;
using System.Threading.Tasks;

namespace MCServerManager.Managers
{
    // Определяет внешний (публичный) IP этого компьютера — тот, который
    // друзьям нужно использовать, чтобы зайти на сервер через интернет
    // (в отличие от "localhost", который работает только у самого хозяина).
    public static class NetworkInfo
    {

        private static readonly HttpClient client = new HttpClient();



        public static async Task<string> GetPublicIpAsync()
        {

            try
            {

                string ip =
                    await client.GetStringAsync("https://api.ipify.org");



                return ip.Trim();

            }
            catch
            {

                return "не удалось определить (нет интернета?)";

            }

        }

    }
}
