using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.Runtime.InteropServices;
using System.ComponentModel;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace SiebelCopy
{
    class Program
    {
        private static int th = 8;
        private static int olderthanday = 3000;
        private static int i = 0; // счетчик обработанных файлов
        private static int k = 0; // счетчик кол-ва итераций 
        private static int j = 0; // кол-во скопированных файлов

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
                Thread.Sleep(10000);
                return;
            }

            int filecount = 30;

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


            string[] destination_dirs = args[1].Split(',');
            foreach (string dir in destination_dirs)
            {
                if (!Directory.Exists(dir))
                {
                    Console.WriteLine("Directory {0} no exist", dir);
                    return;
                }
            }

            foreach (string file in GetFiles(source_dir))
            {
                string sourceFile = System.IO.Path.Combine(source_dir, file);

                List<string> stringList = new List<string>();
                // 1-й этап, копируем данные в каждое назначение
                foreach (string ddir in destination_dirs)
                {
                    string destFile = System.IO.Path.Combine(ddir, file);
                    stringList.Add(destFile);
                }
                string[] array = stringList.ToArray();
                bool flag = false;
                while (!flag)
                {
                    if (th > 0)
                    {
                        flag = true;
                        th--;
                        AsyncFileCopier.AsynFileCopy(sourceFile, array);
                    }
                }
                i++;

                if (i < filecount)
                {
                    if (i % 100 == 0)
                        Console.WriteLine("Source files processed: " + i.ToString());
                    if (j % 100 == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("Files: {0} Skipped: {1}", j.ToString(), k.ToString());
                        Console.ResetColor();
                    }
                }
                else 
                {
                    break;
                }
            }
            Console.WriteLine("Total files: {0} skiped: {1} copied: {2}", i.ToString(), k.ToString(), j.ToString());
            Thread.Sleep(10000);
        }

        public class AsyncFileCopier
        {
            public static void AsynFileCopy(string sourceFile, string[] destFiles) => new Program.AsyncFileCopier.FileCopyDelegate(Program.AsyncFileCopier.FileCopy).BeginInvoke(sourceFile, destFiles, new AsyncCallback(Program.AsyncFileCopier.CallBackAfterFileCopied), (object)null);

            public static void FileCopy(string sourceFile, string[] destFiles)
            {
                try
                {
                    bool flag = false;
                    foreach (string destFile in destFiles)
                    {
                        FileInfo fileInfo = new FileInfo(sourceFile);
                        if (fileInfo.CreationTime < DateTime.Now.AddDays(-olderthanday))
                        {
                            if (!File.Exists(destFile))
                            {
                                File.Copy(sourceFile, destFile);
                                Console.WriteLine("[{2}] File copied: {0} -> {1} ", sourceFile, destFile, DateTime.Now.ToString());
                                flag = true;
                            }
                            else if (fileInfo.Length != new FileInfo(destFile).Length)
                            {
                                File.Copy(sourceFile, destFile, true);
                                Console.WriteLine("[{2}] File copied: {0} -> {1} ", sourceFile, destFile, DateTime.Now.ToString());
                                flag = true;
                            }
                        }
                    }
                    if (!flag)
                    {
                        k++;
                        return;
                    }
                    j++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.ToString());
                }
            }

            public static void CallBackAfterFileCopied(IAsyncResult result) => ++Program.th;

            public delegate void FileCopyDelegate(string sourceFile, string[] destFiles);
        }
    }
}
