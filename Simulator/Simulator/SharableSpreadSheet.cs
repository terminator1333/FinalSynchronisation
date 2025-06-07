using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

class SharableSpreadSheet
{
    private string[,] data;
    private int rows, cols;
    private int userLimit;


    private ReaderWriterLockSlim globalLock = new ReaderWriterLockSlim(); //global lock for sheet changes, for example, adding a row/column (write), or saving(read)


    private ReaderWriterLockSlim[] userLocks; //partitioned lockes responsible for certain parts of the spreadsheet, they are split

    public SharableSpreadSheet(int nRows, int nCols, int nUsers = -1) //initialising the shareablespreadhseet class.
    {
        rows = nRows;
        cols = nCols;
        userLimit = nUsers;
        data = new string[rows, cols];

        InitializeLocks(); //initialising the locks using separate function
    }
        private ReaderWriterLockSlim[] AcquireLocksInOrder(List<(int row, int col)> cells) //a method to get the correct locks in the correct order to avoid deadlock
        {
            var lockPairs = cells.Select(cell => new //creating the new array of locks according to the indexing 
            {
                Index = cell.row * cols + cell.col,
                Lock = GetLockForCell(cell.row, cell.col)
            })
            .OrderBy(pair => pair.Index)
            .ToList();
        
            var uniqueLocks = lockPairs.Select(p => p.Lock).Distinct().ToList(); //removing all locks which are not unique
        
            foreach (var l in uniqueLocks) //getting the write lock
                l.EnterWriteLock();
        
            return uniqueLocks.ToArray(); //returning their array 
        }

    private void InitializeLocks()    //initializing partitioned locks (2nd layer) based on current size and userimit
    {
        int cellCount = rows * cols; //the amount of cells
        int coreCount = Environment.ProcessorCount; //the amount of cores
        int desiredLockCount; //final amonut of locks

        if (userLimit > 0) //for positive userLimit
        {
            if (userLimit > 2 * coreCount && cellCount > 2 * coreCount) // if the cell count and user limit are higher than the amount of cores, use the following formula
            {
                int minUserCell = Math.Min(userLimit, cellCount);
                desiredLockCount = ((minUserCell / coreCount) + coreCount) / coreCount;
            }
            else //else they are in relative range so just take the minimum between them
            {
                desiredLockCount = Math.Min(userLimit, cellCount);
            }
        }
        else //else there is no limit on the amount of users, so take the minimum of
        {
            desiredLockCount = Math.Min(coreCount * 2 + 1, cellCount);
        }


        desiredLockCount = Math.Max(1, desiredLockCount); //making sure the lock count is at least 1


        if (desiredLockCount % 2 == 0) //making sure its odd
        {
            desiredLockCount += 1;
        }
        userLocks = new ReaderWriterLockSlim[desiredLockCount]; //using a new number of locks, and initialising all to a readerwriter lock
        for (int i = 0; i < desiredLockCount; i++)
            userLocks[i] = new ReaderWriterLockSlim();
    }


    private void ResizeLocksIfNeeded()  // recalculating partition locks to adapt to new size, called under the globallock only
    {
        int cellCount = rows * cols; //the amount of cells
        int coreCount = Environment.ProcessorCount; //the amount of cores
        int desiredLockCount; //final amonut of locks

        if (userLimit > 0) //for positive userLimit
        {
            if (userLimit > 2 * coreCount && cellCount > 2 * coreCount) // if the cell count and user limit are higher than the amount of cores, use the following formula
            {
                int minUserCell = Math.Min(userLimit, cellCount);
                desiredLockCount = ((minUserCell / coreCount) + coreCount) / coreCount;
            }
            else //else they are in relative range so just take the minimum between them
            {
                desiredLockCount = Math.Min(userLimit, cellCount);
            }
        }
        else //else there is no limit on the amount of users, so take the minimum of
        {
            desiredLockCount = Math.Min(coreCount * 2 + 1, cellCount);
        }


        desiredLockCount = Math.Max(1, desiredLockCount); //making sure the lock count is at least 1


        if (desiredLockCount % 2 == 0) //making sure its odd
        {
            desiredLockCount += 1;
        }

        if (desiredLockCount != userLocks.Length) //resising only if needed
        {

            foreach (var l in userLocks)
                l.Dispose(); //disposing old locks

            userLocks = new ReaderWriterLockSlim[desiredLockCount];
            for (int i = 0; i < desiredLockCount; i++)
                userLocks[i] = new ReaderWriterLockSlim();
        }
    }


    private ReaderWriterLockSlim GetLockForCell(int row, int col) //mapping a cell to a partition lock index
    {
        int index = ((row * cols + col) % userLocks.Length); //using a simple function for mapping a the place of a cell to a lock
        return userLocks[index]; //returning the readerwriter lock
    }

    private void ValidateIndices(int row, int col) //simple function to check that inserted place is valid
    {
        ValidateCol(col);
        ValidateRow(row);
    }

    private void ValidateRow(int row) //same here, just for rows
    {
        if (row < 0 || row >= rows)
            throw new ArgumentOutOfRangeException($"Invalid row: {row}");
    }

    private void ValidateCol(int col) //same here, just for columns
    {
        if (col < 0 || col >= cols)
            throw new ArgumentOutOfRangeException($"Invalid column: {col}");
    }


public string getCell(int row, int col)
{
    globalLock.EnterReadLock();  //acquiring the global lock first
    try
    {
        ValidateIndices(row, col);  // validating the indicies, if not good, exception will be thrown and global lock will be released
        var cellLock = GetLockForCell(row, col); //getting the local lock
        cellLock.EnterReadLock(); //acquiring the local lock
        try
        {
            return data[row, col]; //returning the requested value, but only after completing the finally blocks
        }
        finally
        {
            cellLock.ExitReadLock(); //releasing the local lock
        }
    }
    finally
    {
        globalLock.ExitReadLock(); //releasing the global lock
    }
}



    public void setCell(int row, int col, string str)    //editing cell operation which needs globalLock read and cell lock write
    {

        globalLock.EnterReadLock(); //since this is a local function, it has global read accessibility
        try
        {
            ValidateIndices(row, col); //validating
            var cellLock = GetLockForCell(row, col);
            cellLock.EnterWriteLock(); //since its a local writer function, it has local write accessibility
            try
            {
                data[row, col] = str; //chaning the value
            }
            finally
            {
                cellLock.ExitWriteLock(); //exiting local write lock
            }
        }
        finally
        {
            globalLock.ExitReadLock(); //exiting global read lock
        }
    }


    public void addRow(int row1) //adding a row to the spreadsheet,this is a global write function
    {


        globalLock.EnterWriteLock(); //accessing the global write lock
        try
        {
        if (row1 < -1 || row1 >= rows) {
            throw new ArgumentOutOfRangeException(); //making sure the indexes are fine, -1 means adding it to the left of 0
        }

            string[,] newData = new string[rows + 1, cols]; //creating new data array with one more row

            for (int r = 0; r <= row1; r++) //all rows up to row1 stay the exact same so we just copy them
                for (int c = 0; c < cols; c++)
                    newData[r, c] = data[r, c];


            for (int r = row1 + 1; r < rows + 1; r++)            //inserting an empty row after row1, shifting remaining rows 1 to the right
                for (int c = 0; c < cols; c++)
                    newData[r, c] = (r == row1 + 1) ? "" : data[r - 1, c];

            data = newData; //replacing the old data and increasing the number of rows
            rows++;

            ResizeLocksIfNeeded(); //adding another lock if needed
        }
        finally
        {
            globalLock.ExitWriteLock(); //releasing the global write lock
        }
    }

    public void addCol(int col1) //very similar to addRow, just for col
    {


        globalLock.EnterWriteLock();
        try
        {
            if (col1 < -1 || col1 >= cols) { //making sure the inserted col1 value is ok
                throw new ArgumentOutOfRangeException();
            }

            string[,] newData = new string[rows, cols + 1];

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c <= col1; c++)
                    newData[r, c] = data[r, c];
                newData[r, col1 + 1] = "";
                for (int c = col1 + 1; c < cols; c++)
                    newData[r, c + 1] = data[r, c];
            }

            data = newData;
            cols++;

            ResizeLocksIfNeeded();
        }
        finally
        {
            globalLock.ExitWriteLock();
        }
    }


public void exchangeRows(int row1, int row2)
{
    globalLock.EnterWriteLock(); //getting the global write lock
    try
    {
        ValidateRow(row1); //making sure the numbers are fine
        ValidateRow(row2);
        if (row1==row2) return; //if its the same row, exit

    
        var cells = new List<(int row, int col)>(); //list of all cells we need to lock
        for (int c = 0; c < cols; c++)
        {
            cells.Add((row1, c)); //adding the cells
            cells.Add((row2, c));
        }
        var locks = AcquireLocksInOrder(cells); //getting all the cell locks using the needed method
        try
        {

            for (int c = 0; c < cols; c++)//swapping the data in the two rows
            {
                string temp =data[row1, c]; //temp val to save the seen data before replacing
                data[row1, c]=data[row2, c];
                data[row2, c]= temp;
            }
        }
        finally
        {

            for (int i = locks.Length - 1; i >= 0; i--)
            {
                locks[i].ExitWriteLock();//releasing all locks
            }
        }
    }
    finally
    {
        globalLock.ExitWriteLock(); //releasing the global lock
    }
}



public void exchangeCols(int col1, int col2)
{
   globalLock.EnterWriteLock(); //getting the global write lock
    try
    {
        ValidateCol(col1); //making sure the numbers are fine
        ValidateCol(col2);
        if (col1==col2) return; //if its the same col, exit

        var cells = new List<(int row, int col)>(); //getting all the needed cells
        for (int r = 0; r < rows; r++)
        {
            cells.Add((r, col1));
            cells.Add((r, col2));
        }

        var locks = AcquireLocksInOrder(cells); // get all locks in correct global order
        try
        {
            for (int c = 0; c < rows; c++)//swapping the data in the two cols
            {
                string temp =data[c, col1]; //temp val to save the seen data before replacing
                data[c, col1]=data[c, col2];
                data[c, col2]= temp;
            }
        }
        finally
        {

            for (int i = locks.Length - 1; i >= 0; i--)
            {
                locks[i].ExitWriteLock();//releasing all locks
            }
        }
    }
    finally
    {
        globalLock.ExitWriteLock(); //releasing the global lock
    }
}

    public Tuple<int, int> searchString(string str) //search string function
    {
        globalLock.EnterReadLock(); //getting global read lock

        try
        {
            Tuple<int, int> result = null;
            for (int r = 0; r < rows; r++) //going through each row and checking if its the string we are looking for
            {
                for (int c = 0; c < cols; c++)
                {
                    var cellLock = GetLockForCell(r, c);
                    cellLock.EnterReadLock();
                    try
                    {
                        if (data[r, c] == str)
                            return Tuple.Create(r, c);   //returning the tuple, the finally segements will run anyways
                    }
                    finally
                    {
                        cellLock.ExitReadLock();//exiting this local lock
                    }
                }
            }
            return null;                                  //if one was not found, return null
        }
        finally
        {
            globalLock.ExitReadLock(); //finally release the global lock
        }
    }


    public void save(string filename) //the save operation, it requires a global write lock
    {
        globalLock.EnterWriteLock(); //accessing the global write lock
        try
        {
            using (StreamWriter sw = new StreamWriter(filename)) //opening the file for writing
            {
                for (int r = 0; r < rows; r++) //iterating through each row
                {
                    List<string> rowValues = new List<string>(); //getting the row values and storing in a list
                    for (int c = 0; c < cols; c++)
                        rowValues.Add(data[r, c] ?? ""); //adding a cell value or empty if null
                    sw.WriteLine(string.Join(",", rowValues)); // writing row to file, separated by a comma
                }
            }
        }
        finally
        {
            globalLock.ExitWriteLock(); // releasing the global write lock after saving
        }
    }

    public void load(string filename) //load operation, it also requires a global write lock
    {
        globalLock.EnterWriteLock(); //accessing the global write lock
        try
        {
            if (!File.Exists(filename))
                throw new FileNotFoundException($"The file '{filename}' was not found.");

            var lines = File.ReadAllLines(filename); //reading all lines from file
            int newRows = lines.Length; //getting the number of rows and cols
            int newCols = 0;

            newCols = lines[0].Split(',').Length; // getting the number of columns from first line

            var newData = new string[newRows, newCols]; // allocating a new data array
            for (int r = 0; r < newRows; r++) // filling data array from file
            {
                var parts = lines[r].Split(',');
                for (int c = 0; c < newCols; c++)
                {
                    newData[r, c] = (c < parts.Length) ? parts[c] : ""; // handling missing values
                }
            }

            data = newData; // updating the spreadsheet object variables
            rows = newRows;
            cols = newCols;

            ResizeLocksIfNeeded(); // resizing local locks to match new amount of rows and cols
        }
        finally
        {
            globalLock.ExitWriteLock(); // releasing the global write lock after loading
        }

    }
}
