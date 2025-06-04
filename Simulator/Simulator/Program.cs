using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

class Simulator
{

    private static int ROWS, COLS, N_THREADS, N_OPS, MS_SLEEP; //command line paramters

   
    private static SharableSpreadSheet sheet; //the spreadsheet object
    private static readonly object consoleLock = new object(); // lock object for output

    private static int rowCount, colCount; //the amount of rows and columns

    private static readonly ThreadLocal<Random> rnd =
        new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode())); //random number generator for each thread

    static void Main(string[] args)
    {
        if    ( !int.TryParse(args[0], out ROWS) || ROWS < 0 || //making sure the input is ok and non negative and that the number of threads is positive
    !int.TryParse(args[1], out COLS) || COLS < 0 ||
    !int.TryParse(args[2], out N_THREADS) || N_THREADS <= 0 ||
    !int.TryParse(args[3], out N_OPS) || N_OPS < 0 ||
    !int.TryParse(args[4], out MS_SLEEP) || MS_SLEEP < 0)
        {
            Console.Error.WriteLine("Usage: Simulator <rows> <cols> <nThreads> <nOperations> <mssleep>");
            return;
        }

        sheet = new SharableSpreadSheet(ROWS, COLS); //creating the new shareable spreadsheet
        rowCount = ROWS;
        colCount = COLS;

        FillInitialData(); //filling the spreadsheet with strings 

        Log($"*** Spreadsheet {ROWS}×{COLS} created. Launching {N_THREADS} users… ***");

        var threads = new List<Thread>(); //creating a list of threads

        for (int t = 0; t < N_THREADS; t++)
        {
            var thread = new Thread(ThreadWorker) { IsBackground = true }; //initialising each thread
            thread.Start(); //starting the thread and adding it to the list
            threads.Add(thread);
        }

        threads.ForEach(th => th.Join()); //joining them after ending the simulation

        Log("*** Simulation finished. ***");
    }

    private static void ThreadWorker()
    {
        int id = Thread.CurrentThread.ManagedThreadId; //the thread id

        for (int i = 0; i < N_OPS; i++)
        {
            PerformRandomOperation(id); //preforming a random operation N_OPS times and then sleeping
            Thread.Sleep(MS_SLEEP);
        }
    }


    private static void PerformRandomOperation(int userId) //generating random value and preforming the function accordignly
    {
        int op = rnd.Value.Next(7);

        switch (op)
        {
            case 0: GetCell(userId); break;
            case 1: SetCell(userId); break;
            case 2: AddRow(userId); break;
            case 3: AddCol(userId); break;
            case 4: ExchangeRows(userId); break;
            case 5: ExchangeCols(userId); break;
            case 6: SearchString(userId); break;
        }
    }

    private static void GetCell(int uid)
    {
        int r = rnd.Value.Next(rowCount);
        int c = rnd.Value.Next(colCount);
        string val = sheet.getCell(r, c);
        Log(uid, $"read \"{val}\" from [{r},{c}]"); //printing the data and index of the cell
    }

    private static void SetCell(int uid)
    {
        int r = rnd.Value.Next(rowCount);
        int c = rnd.Value.Next(colCount);
        string newVal = $"v_{Guid.NewGuid():N}".Substring(0, 6);
        sheet.setCell(r, c, newVal);
        Log(uid, $"wrote \"{newVal}\" to [{r},{c}]"); //setting the cell and printing the new info
    }

    private static void AddRow(int uid)
    {
        int pos = rnd.Value.Next(-1, rowCount);
        sheet.addRow(pos);
        Interlocked.Increment(ref rowCount);
        Log(uid, $"added a row after {pos}"); //adding row and printing new info
    }

    private static void AddCol(int uid)
    {
        int pos = rnd.Value.Next(-1, colCount);
        sheet.addCol(pos);
        Interlocked.Increment(ref colCount);
        Log(uid, $"added a column after {pos}");//adding column and printing new info
    }

    private static void ExchangeRows(int uid)
    {
        if (rowCount < 2) return;
        int r1 = rnd.Value.Next(rowCount);
        int r2 = rnd.Value.Next(rowCount);
        if (r1 == r2) r2 = (r2 + 1) % rowCount; 
        sheet.exchangeRows(r1, r2);
        Log(uid, $"swapped rows {r1} & {r2}"); //exchanging the rows and printing
    }

    private static void ExchangeCols(int uid)
    {
        if (colCount < 2) return;
        int c1 = rnd.Value.Next(colCount);
        int c2 = rnd.Value.Next(colCount);
        if (c1 == c2) c2 = (c2 + 1) % colCount;
        sheet.exchangeCols(c1, c2);
        Log(uid, $"swapped cols {c1} & {c2}"); //exchanging the columns and printing
    }
    private static void SearchString(int uid) //using the searchstring func from the spreadsheet class and printing 
    {
        int r = rnd.Value.Next(rowCount);
        int c = rnd.Value.Next(colCount);
        string target = $"cell_{r}_{c}";
        var loc = sheet.searchString(target);

        if (loc != null)
            Log(uid, $"found \"{target}\" in [{loc.Item1},{loc.Item2}]");
        else
            Log(uid, $"did not find \"{target}\"");
    }

    private static void FillInitialData() //filling for initial data
    {
        for (int r = 0; r < ROWS; r++)
            for (int c = 0; c < COLS; c++)
                sheet.setCell(r, c, $"cell_{r}_{c}");
    }

    private static void Log(string msg) //special locked log functions for output
    {
        lock (consoleLock)
            Console.WriteLine(msg);
    }

    private static void Log(int userId, string action)
    {
        string stamp = DateTime.Now.ToString("HH:mm:ss.fff");
        lock (consoleLock)
            Console.WriteLine($"User[{userId}] {stamp}  {action}");
    }
}
