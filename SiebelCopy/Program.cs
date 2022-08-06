using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;
using System.ComponentModel;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace SiebelCopy
{
    class Program
    {
        private static string MakePath(string path)
        {
            return Path.Combine(path, "*");
        }

        /// <summary>
        /// Возвращает список файлов или каталогов находящихся по заданному пути.
        /// </summary>
        /// <param name="path">Путь для которого нужно возвратать список.</param>
        /// <param name="isGetDirs">
        /// Если true - функция возвращает список каталогов, иначе файлов.
        /// </param>
        /// <returns>Список файлов или каталогов.</returns>
        private static IEnumerable<string> GetInternal(string path, bool isGetDirs)
        {
            // Структура в которую функции FindFirstFile и FindNextFileвозвращают
            // информацию о текущем файле.
            WIN32_FIND_DATA findData;
            // Получаем информацию о текущем файле и хэндл перечислителя виндовс.
            // Этот хэндл требуется передавать функции FindNextFile для плучения
            // следующих файлов.
            IntPtr findHandle = FindFirstFile(MakePath(path), out findData);

            // Хреновый хэндл говорит, о том, что произошел облом. Следовательно
            // нужно вынуть информацию об ошибке и перепаковать ее в исключение.
            if (findHandle == INVALID_HANDLE_VALUE)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                do
                    if (isGetDirs
                        ? (findData.dwFileAttributes & FileAttributes.Directory) != 0
                        : (findData.dwFileAttributes & FileAttributes.Directory) == 0)
                        yield return findData.cFileName;
                while (FindNextFile(findHandle, out findData));
            }
            finally
            {
                FindClose(findHandle);
            }
        }

        /// <summary>
        /// Возвращает список файлов для некоторого пути.
        /// </summary>
        /// <param name="path">
        /// Каталог для которого нужно получить список файлов.
        /// </param>
        /// <returns>Список файлов каталога.</returns>
        public static IEnumerable<string> GetFiles(string path)
        {
            return GetInternal(path, false);
        }

        /// <summary>
        /// Возвращает список каталогов для некоторого пути. Функция не перебирает
        /// вложенные подкаталоги!
        /// </summary>
        /// <param name="path">
        /// Каталог для которого нужно получить список подкаталогов.
        /// </param>
        /// <returns>Список файлов каталога.</returns>
        public static IEnumerable<string> GetDirectories(string path)
        {
            return GetInternal(path, true);
        }

        /// <summary>
        /// Функция возвращает список относительных путей ко всем подкаталогам
        /// (в том числе и вложенным) заданного пути.
        /// </summary>
        /// <param name="path">Путь для которого унжно получить подкаталоги.</param>
        /// <returns>Список подкатлогов.</returns>
        public static IEnumerable<string> GetAllDirectories(string path)
        {
            // Сначала перебираем подкаталоги первого уровня вложенности...
            foreach (string subDir in GetDirectories(path))
            {
                // игнорируем имя текущего каталога и родительского.
                if (subDir == ".." || subDir == ".")
                    continue;

                // Комбинируем базовый путь и имя подкаталога.
                string relativePath = Path.Combine(path, subDir);

                // возвращам пользователю относительный путь.
                yield return relativePath;

                // Создаем, рекурсивно, итератор для каждого подкаталога и...
                // возвращаем каждый его элемент в качестве элементов текущего итератоа.
                // Этот прием позволяет обойти ограничение итераторов C# 2.0 связанное
                // с нвозможностью вызовов "yield return" из функций вызваемых из 
                // функции итератора. К сожалению это приводит к созданию временного
                // вложенного итератора на каждом шаге рекурсии, но затраты на создание
                // такого объекта относительно не велики, а удобство очень даже ощутимо.
                foreach (string subDir2 in GetAllDirectories(relativePath))
                    yield return subDir2;
            }
        }

        #region Импорт из kernel32

        private const int MAX_PATH = 260;

        [Serializable]
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        [BestFitMapping(false)]
        private struct WIN32_FIND_DATA
        {
            public FileAttributes dwFileAttributes;
            public FILETIME ftCreationTime;
            public FILETIME ftLastAccessTime;
            public FILETIME ftLastWriteTime;
            public int nFileSizeHigh;
            public int nFileSizeLow;
            public int dwReserved0;
            public int dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternate;
        }

        [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr FindFirstFile(string lpFileName,
            out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool FindNextFile(IntPtr hFindFile,
            out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FindClose(IntPtr hFindFile);

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        #endregion


        static void Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: [SiebelRWMove.exe] source_dir[,mirror_dir] destination_dir[,destination_dir[,destination_dir...]] filecount [olderthanday default:30]");
                return;
            }

            int filecount, olderthanday = 30;

            // Проверяем число

            if (!Int32.TryParse(args[2], out filecount))
            {
                Console.WriteLine("Attempted conversion filecount of '{0}' failed.",
                               args[2] == null ? "<null>" : args[2]);
                return;
            }

            if (args.Length >= 4)
                if (!Int32.TryParse(args[3], out olderthanday))
                {
                    Console.WriteLine("Attempted conversion olderthanday of '{0}' failed.",
                                   args[3] == null ? "<null>" : args[3]);
                    return;
                }


            // Проверяем директорию-источник
            string[] source_dirs = args[0].Split(',');
            string source_dir = source_dirs[0];
            string mirror_dir = null;

            if (source_dirs.Length >= 2) { mirror_dir = source_dirs[1]; }
            if (!Directory.Exists(source_dir))
            {
                Console.WriteLine("Directory {0} no exist", args);
                return;
            }


            int tick = 0; // Если процессы бэкапа и перемещения не выполнятся за 1 час, то приложение закроется
            // Проверяем пути назначения, что существуют
            string[] destination_dirs = args[1].Split(',');
            foreach (string dir in destination_dirs)
            {
                if (!Directory.Exists(dir))
                {
                    Console.WriteLine("Directory {0} no exist", dir);
                    return;
                }
            }

            int i = 0; // счетчик обработанных файлов
            int k = 1; // счетчик кол-ва итераций 
            int j = 0; // кол-во скопированных файлов


            // Процесс перемещения файлов
            foreach (string file in GetFiles(source_dir))
            {
                string sourceFile = System.IO.Path.Combine(source_dir, file);
                FileInfo f1 = new FileInfo(sourceFile);

                // Файлы старше 30 дней
                if (f1.CreationTime < DateTime.Now.AddDays(-olderthanday))
                {
                    // 1-й этап, копируем данные в каждое назначение
                    foreach (string ddir in destination_dirs)
                    {
                        string destFile = System.IO.Path.Combine(ddir, file);

                        if (!File.Exists(destFile))
                        {
                            try
                            {
                                File.Copy(sourceFile, destFile);
                                Console.WriteLine("[{2}] File copied: {0} -> {1} ", sourceFile, destFile, DateTime.Now.ToString());
                                j++;
                            }
                            catch (Exception E)
                            {
                                Console.WriteLine(E);
                            }
                        }
                        else
                        {
                            // Перезаписываем если размер разный
                            if (f1.Length != (new FileInfo(destFile)).Length)
                                try
                                {
                                    File.Copy(sourceFile, destFile, true);
                                }
                                catch (Exception E)
                                {
                                    Console.WriteLine(E);
                                }
                        }
                    }

                    bool iscopyed = false;
                    // 2-й этап проверяем, что данные скопированы
                    foreach (string ddir in destination_dirs)
                    {
                        string destFile = System.IO.Path.Combine(ddir, file);

                        // Если файла не существует, то прерываем цикл
                        if (!File.Exists(destFile))
                        {
                            Console.WriteLine("[{2}] Files error copy from {0} to {1}: ", source_dir, ddir, DateTime.Now.ToString());
                            iscopyed = false;
                            break;
                        }

                        // Если файл существует и размер равный, ставим триггер, что данные скопированы
                        if (File.Exists(destFile))
                            if (f1.Length == (new FileInfo(destFile)).Length)
                            {
                                iscopyed = true;
                            }
                    }

                    // Если данные скопированы, удаляем файл из источника
                    if (iscopyed)
                    {
                        /*
                        File.Delete(sourceFile);
                        if (mirror_dir != null)
                        {
                            string mirrorFile = System.IO.Path.Combine(mirror_dir, file);
                            File.Delete(mirrorFile);
                            Console.WriteLine("[{1}] File {0} was deleted", mirrorFile, DateTime.Now.ToString());
                        }
                        Console.WriteLine("[{1}] File {0} was deleted", sourceFile, DateTime.Now.ToString());
                        */
                    }


                    i++;

                    if (i >= filecount)
                    {
                        break;
                    }

                    if (i % 100 == 0)
                        Console.WriteLine("Files processed: " + i.ToString());
                }
                else
                {
                    Console.WriteLine("File {0} was fresh {1}", sourceFile, f1.CreationTime.ToString());
                }

                if (k % 100 == 0)
                    Console.WriteLine("Файлов: {0} Пропущено: {1}", k.ToString(), (k - i).ToString());

                k++;
            }
            Console.WriteLine("Total files: {0} skiped: {1} processed: {2} copied: {3}", k.ToString(), (k - i).ToString(), i.ToString(), (j / ((destination_dirs.Count() == 0) ? 1 : destination_dirs.Count())).ToString());
        }
    }
}
