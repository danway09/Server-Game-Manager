using System;
using System.IO;
using System.Threading;

namespace MCServerManager.Managers
{
    // Удаление временных файлов (инсталляторов) может ненадолго упереться
    // в "файл занят другим процессом" — JVM может на долю секунды придержать
    // хендл после завершения, антивирус может в этот момент сканировать
    // свежескачанный jar. Раньше единичная неудачная попытка удаления
    // рушила ВСЮ установку, хотя сама установка уже прошла успешно.
    // Тут просто пробуем несколько раз с паузой, а если не вышло —
    // тихо сдаёмся, это не критично (мусорный файл можно почистить и вручную).
    public static class FileHelper
    {

        public static void TryDeleteWithRetries(string path, int attempts = 5, int delayMs = 300)
        {

            for (int i = 0; i < attempts; i++)
            {

                try
                {

                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }



                    return;

                }
                catch
                {

                    Thread.Sleep(delayMs);

                }

            }

        }




        // То же самое, но для целой папки — при удалении сервера файлы
        // внутри (например, exe TShock/java.exe только что завершившегося
        // процесса) могут на долю секунды оставаться занятыми
        public static void TryDeleteDirectoryWithRetries(string path, int attempts = 5, int delayMs = 300)
        {

            Exception? lastError = null;



            for (int i = 0; i < attempts; i++)
            {

                try
                {

                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }



                    return;

                }
                catch (Exception ex)
                {

                    lastError = ex;


                    Thread.Sleep(delayMs);

                }

            }



            if (lastError != null)
            {

                throw new Exception(
                    "Не удалось удалить папку сервера — возможно, какой-то " +
                    "файл внутри до сих пор занят другим процессом. " +
                    "Попробуй закрыть все связанные процессы и удалить снова.\n\n" +
                    lastError.Message
                );

            }

        }

    }
}