﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using MySql.Data.MySqlClient;
using System.IO;
using FirstTeamScouter_Server.Utilities;

namespace FirstTeamScouter_Server
{
    public partial class ImportDataForm : Form
    {
        private long compID;
        private string importPath;
        private MySQL_ErrorCodes mySQLErrors;

        private static string[] tabletSpecificTables = {
                                                           "notes_data",
                                                           "picture_data",
                                                           "robot_data",
                                                           "team_match_transaction"
                                                       };

        public ImportDataForm(long competition_id)
        {
            mySQLErrors = new MySQL_ErrorCodes();
            this.compID = competition_id;
            this.importPath = Properties.Settings.Default.ImportPath;

            InitializeComponent();
            LoadCompetitions();
            loadFileList();

            lblPath.Text = importPath;
        }

        public void loadFileList()
        {
            if (Directory.Exists(this.importPath))
            {
                CheckedListBox.ObjectCollection items = chkFileList.Items;
                items.Clear();
                IEnumerable<string> files = Directory.EnumerateFiles(this.importPath, "*.xml", SearchOption.TopDirectoryOnly);
                foreach (string fileName in files)
                {
                    string[] fn = fileName.Split('\\');
                    string name = fn[fn.Length - 1];
                    items.Add(name);
                }
            }
            chkFileList.Refresh();
            Console.Out.WriteLine("File List loaded");
        }

        public void LoadCompetitions()
        {
            MySqlConnection connection = new MySqlConnection(Utils.getConnectionString());
            MySqlCommand cmd;
            bool connectionAvailable = Utils.openConnection(connection, lblStatus);

            if (connectionAvailable)
            {
                try
                {
                    cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT _id, competition_name, competition_location FROM competition_data";
                    MySqlDataAdapter adap = new MySqlDataAdapter(cmd);
                    DataSet ds = new DataSet();
                    adap.Fill(ds);
                    cmbCompetitionName.DataSource = ds.Tables[0].DefaultView;
                    cmbCompetitionName.ValueMember = "_id";
                    cmbCompetitionName.DisplayMember = "competition_name";
                    cmbCompetitionName.SelectedValue = this.compID;
                }
                catch (MySql.Data.MySqlClient.MySqlException)
                {
                    Console.Out.WriteLine("Unable to open MySQL connection - check if the database is installed and running!");
                }
                catch (Exception)
                {
                    throw;
                }
                finally
                {
                    if (connection.State == System.Data.ConnectionState.Open)
                    {
                        connection.Close();
                    }
                }
            }
        }

        public void ChooseFolder()
        {
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {
                this.importPath = folderBrowserDialog1.SelectedPath;
                Properties.Settings.Default.ImportPath = importPath;
                lblPath.Text = importPath;
                Properties.Settings.Default.Save();
            }
        }

        private void btnSetPath_Click(object sender, EventArgs e)
        {
            ChooseFolder();
            loadFileList();
        }

        private void btnImportSelected_Click(object sender, EventArgs e)
        {
            List<string> items = chkFileList.CheckedItems.Cast<string>().ToList();
            lblStatus.Text = "Items checked: " + items.Count;

            importItems(items);
        }

        private void importItems(List<string> stuffToImport)
        {
            for (int i = 0; i < stuffToImport.Count; i++)
            {
                string fileName = stuffToImport[i].ToString();
                Console.Out.WriteLine("Importing " + fileName);

                long compID = Utils.getLongIDFromComboSelectedValue(cmbCompetitionName, lblStatus);

                string filePath = this.importPath + "\\" + fileName;
                DataSet ds = getXmlDataSet(filePath);

                if (hasIDColumn(ds))
                {
                    sqlUpdateRecord(fileName, filePath);
                }
                else
                {
                    sqlInsertRecord(fileName, filePath);
                }
            }
        }

        private void sqlInsertRecord(string fileName, string filePath)
        {
            DataSet ds = getXmlDataSet(filePath);

            MySqlConnection connection = new MySqlConnection(Utils.getConnectionString());
            MySqlCommand cmd;
            bool connectionAvailable = Utils.openConnection(connection, lblStatus);

            if (connectionAvailable)
            {
                try
                {
                    cmd = connection.CreateCommand();

                    string tableName = getTableName(ds);

                    if (!String.IsNullOrEmpty(tableName))
                    {
                        Console.Out.WriteLine("Table Name: " + tableName);
                        DataTable dataTable = ds.Tables["row"];

                        string prefix = "INSERT INTO " + tableName + "(";
                        string postfix = ") VALUES(";

                        int cnt = dataTable.Rows[0].Table.Columns.Count - 1;
                        string[] colParams = new string[cnt];
                        for (int col = 0; col < cnt; col++)
                        {
                            var column = dataTable.Rows[0].Table.Columns[col];
                            Console.Out.WriteLine("Column: " + column);
                            prefix += column;

                            string colParam = "@" + column;
                            postfix += colParam;
                            colParams[col] = colParam;

                            if (col != cnt - 1)
                            {
                                prefix += ", ";
                                postfix += ", ";
                            }
                            else
                            {
                                postfix += ")";
                            }
                        }

                        cmd.CommandText = prefix + postfix;
                        cmd.Prepare();

                        int numRows = 0;
                        for (int col = 0; col < dataTable.Rows[0].Table.Columns.Count - 1; col++)
                        {
                            var param = colParams[col];
                            var val = dataTable.Rows[0][col];
                            cmd.Parameters.AddWithValue(param, val);
                        }
                        foreach (DataRow dataRow in dataTable.Rows)
                        {
                            for (int col = 0; col < dataRow.Table.Columns.Count - 1; col++)
                            {
                                cmd.Parameters[colParams[col]].Value = dataRow[col];
                            }
                            numRows += cmd.ExecuteNonQuery();
                        }

                        if (numRows > 0)
                        {
                            string backupPath = importPath + "\\backup";
                            backupFile(filePath);
                            //string backupFilePath = backupPath + "\\" + fileName;
                            //if (File.Exists(filePath))
                            //{
                            //    if (!Directory.Exists(backupPath))
                            //    {
                            //        Directory.CreateDirectory(backupPath);
                            //    }
                            //    if (File.Exists(backupFilePath))
                            //    {
                            //        File.Delete(backupFilePath);
                            //    }
                            //    File.Move(filePath, backupFilePath); // what if file exists in backup directory???
                            //    Console.Out.WriteLine("File moved");
                            //}
                            loadFileList();
                        }
                    }
                }
                catch (MySql.Data.MySqlClient.MySqlException ex)
                {
                    string reason = (mySQLErrors.errorCodes.ContainsKey(ex.Number)) ? mySQLErrors.errorCodes[ex.Number] : "check if the database is installed and running!";

                    string message = "Unable to open MySQL connection - " + reason;
                    Console.Out.WriteLine(message);
                    Console.Out.WriteLine(ex.Message);

                    lblStatus.Text = message;
                }
                catch (IOException iox)
                {
                    string message = "Unable to move file: " + filePath;
                    Console.Out.WriteLine(message);
                    Console.Out.WriteLine(iox.Message);
                    lblStatus.Text = message;
                }
                catch (Exception ex)
                {
                    Console.Out.WriteLine(ex.Message);
                    throw;
                }
                finally
                {
                    if (connection.State == System.Data.ConnectionState.Open)
                    {
                        connection.Close();
                    }
                }
            }
        }

        private void sqlUpdateRecord(string fileName, string filePath)
        {
            DataSet ds = getXmlDataSet(filePath);

            MySqlConnection connection = new MySqlConnection(Utils.getConnectionString());
            MySqlCommand cmd;
            bool connectionAvailable = Utils.openConnection(connection, lblStatus);

            if (connectionAvailable)
            {
                try
                {
                    cmd = connection.CreateCommand();

                    string tableName = getTableName(ds);

                    if (!String.IsNullOrEmpty(tableName))
                    {
                        Console.Out.WriteLine("Table Name: " + tableName);
                        DataTable dataTable = ds.Tables["row"];

                        string prefix = "UPDATE " + tableName + " SET ";
                        string postfix = " WHERE _id=@_id";

                        if(tabletSpecificTables.Contains<string>(tableName))
                        {
                            postfix += " AND tablet_id=@tablet_id";
                        }

                        string insertPrefix = "INSERT INTO " + tableName + "(";
                        string insertPostfix = ") VALUES(";
                        
                        int cnt = dataTable.Rows[0].Table.Columns.Count - 1;
                        string[] colParams = new string[cnt];
                        for (int col = 0; col < cnt; col++)
                        {
                            var column = dataTable.Rows[0].Table.Columns[col];
                            Console.Out.WriteLine("Column: " + column);
                            insertPrefix += column;
                            
                            string colParam = "@" + column;
                            insertPostfix += colParam;
                            colParams[col] = colParam;
                            
                            string tuple = column + " = " + colParam;

                            prefix += tuple;

                            
                            if (col != cnt - 1)
                            {
                                prefix += ", ";
                                insertPrefix += ", ";
                                insertPostfix += ", ";
                            }
                            else
                            {
                                insertPostfix += ")";
                            }
                        }

                        cmd.CommandText = prefix + postfix;
                        cmd.Prepare();

                        int numRows = 0;
                        int numRowsUpdated = 0;
                        for (int col = 0; col < dataTable.Rows[0].Table.Columns.Count - 1; col++)
                        {
                            var param = colParams[col];
                            var val = dataTable.Rows[0][col];
                            val = (val == null || ((string)val).Equals("")) ? "NO VALUE" : val;
                            cmd.Parameters.AddWithValue(param, val);
                        }
                        foreach (DataRow dataRow in dataTable.Rows)
                        {
                            numRows++;
                            for (int col = 0; col < dataRow.Table.Columns.Count - 1; col++)
                            {
                                cmd.Parameters[colParams[col]].Value = dataRow[col];
                            }
                            numRowsUpdated += cmd.ExecuteNonQuery();

                            if (numRows != numRowsUpdated)
                            {
                                cmd.CommandText = insertPrefix + insertPostfix;
                                cmd.ExecuteNonQuery();
                                long id = cmd.LastInsertedId;
                                if (id != -1)
                                {
                                    lblStatus.Text = "Row could not be updated, but was inserted";
                                    numRowsUpdated++;
                                }
                                else
                                {
                                    lblStatus.Text = "Row could not be updated NOR inserted";
                                }
                            }
                        }

                        if (numRows == numRowsUpdated)
                        {
                            backupFile(filePath);
                            loadFileList();
                        }
                        else
                        {
                            lblStatus.Text = (numRows - numRowsUpdated) + " of " + numRows + " row(s) not imported";
                        }
                    }
                }
                catch (MySql.Data.MySqlClient.MySqlException ex)
                {
                    string reason = (mySQLErrors.errorCodes.ContainsKey(ex.Number)) ? mySQLErrors.errorCodes[ex.Number] : "Error Code: " + ex.Number;

                    string message = "Unable to open MySQL connection - " + reason;
                    Console.Out.WriteLine(message);
                    Console.Out.WriteLine(ex.Message);

                    lblStatus.Text = message;
                }
                catch (IOException iox)
                {
                    string message = "Unable to move file: " + filePath;
                    Console.Out.WriteLine(message);
                    Console.Out.WriteLine(iox.Message);
                    lblStatus.Text = message;
                }
                catch (Exception ex)
                {
                    Console.Out.WriteLine(ex.Message);
                    throw;
                }
                finally
                {
                    if (connection.State == System.Data.ConnectionState.Open)
                    {
                        connection.Close();
                    }
                }
            }
        }

        private void backupFile(string filePath)
        {
            string[] pathParts = filePath.Split(new char[]{'\\'});
            string fileName = pathParts[pathParts.Length - 1];
            string backupPath = importPath + "\\backup";
            string backupFilePath = backupPath + "\\" + fileName;
            if (File.Exists(filePath))
            {
                if (!Directory.Exists(backupPath))
                {
                    Directory.CreateDirectory(backupPath);
                }
                if (File.Exists(backupFilePath))
                {
                    File.Delete(backupFilePath);
                }
                File.Move(filePath, backupFilePath); // what if file exists in backup directory???
                Console.Out.WriteLine("File moved");
            }
        }

        private static DataSet getXmlDataSet(string filePath)
        {
            DataSet ds = new DataSet();
            if (File.Exists(filePath))
            {
                ds.ReadXml(filePath);
            }
            return ds;
        }

        private static string getTableName(DataSet ds)
        {
            DataTable dt = ds.Tables["table"];
            int nameIndex = -1;
            int columnCount = dt.Columns.Count;
            for (int n = 0; n < columnCount; n++)
            {
                string colName = dt.Columns[n].ToString();
                if (colName.Contains("name"))
                {
                    nameIndex = n;
                    break;
                }
            }

            string tableName = (nameIndex >= 0 && dt.Rows.Count > 0) ? dt.Rows[0][nameIndex].ToString() : null;
            return tableName;
        }

        private static bool hasIDColumn(DataSet ds)
        {
            DataTable dt = ds.Tables["row"];
            int idIndex = -1;
            int columnCount = dt.Columns.Count;
            for (int i = 0; i < columnCount; i++)
            {
                string colName = dt.Columns[i].ToString();
                if (colName.Equals("_id"))
                {
                    idIndex = i;
                    break;
                }
            }

            return idIndex != -1;
        }

        private void btnIgnoreAll_Click(object sender, EventArgs e)
        {
            foreach (string fileName in chkFileList.Items)
            {
                Console.Out.WriteLine("Ignoring " + fileName);

                string filePath = this.importPath + "\\" + fileName;
                backupFile(filePath);
                //string backupPath = importPath + "\\backup";
                //string backupFilePath = backupPath + "\\" + fileName;
                //if (File.Exists(filePath))
                //{
                //    if (!Directory.Exists(backupPath))
                //    {
                //        Directory.CreateDirectory(backupPath);
                //    }
                //    File.Move(filePath, backupFilePath); // what if file exists in backup directory???
                //}
            }
            loadFileList();
        }

        private void btnIgnoreSelected_Click(object sender, EventArgs e)
        {
            CheckedListBox.CheckedItemCollection checkedStuff = chkFileList.CheckedItems;
            lblStatus.Text = "Items checked: " + checkedStuff.Count;

            foreach (string fileName in checkedStuff)
            {
                Console.Out.WriteLine("Ignoring " + fileName);

                string filePath = this.importPath + "\\" + fileName;
                backupFile(filePath);
                //string backupPath = importPath + "\\backup";
                //string backupFilePath = backupPath + "\\" + fileName;
                //if (File.Exists(filePath))
                //{
                //    if (!Directory.Exists(backupPath))
                //    {
                //        Directory.CreateDirectory(backupPath);
                //    }
                //    File.Move(filePath, backupFilePath); // what if file exists in backup directory???
                //}
            }
            loadFileList();
        }

        private void btnImportAll_Click(object sender, EventArgs e)
        {
            //CheckedListBox.ObjectCollection stuffToImport = chkFileList.Items;
            //lblStatus.Text = "Total Items: " + stuffToImport.Count;

            List<string> items = chkFileList.Items.Cast<string>().ToList();
            lblStatus.Text = "Total items: " + items.Count;

            importItems(items);


            /*for (int i = 0; i < stuffToImport.Count; i++)
            {
                string fileName = stuffToImport[i].ToString();
                Console.Out.WriteLine("Importing " + fileName);

                long compID = Utils.getLongIDFromComboSelectedValue(cmbCompetitionName, lblStatus);

                string filePath = this.importPath + "\\" + fileName;
                DataSet ds = new DataSet();
                if (File.Exists(filePath))
                {
                    ds.ReadXml(filePath);
                }

                MySqlConnection connection = new MySqlConnection(Utils.getConnectionString());
                MySqlCommand cmd;
                bool connectionAvailable = Utils.openConnection(connection, lblStatus);

                if (connectionAvailable)
                {
                    try
                    {
                        cmd = connection.CreateCommand();

                        DataTable dt = ds.Tables["table"];
                        string tableName = (dt.Columns.Count > 1) ? dt.Rows[0][1].ToString() : "";
                        Console.Out.WriteLine("Table Name: " + tableName);
                        DataTable dataTable = ds.Tables["row"];
                        if (!String.IsNullOrEmpty(tableName))
                        {
                            string prefix = "INSERT INTO " + tableName + "(";
                            string postfix = ") VALUES(";

                            int cnt = dataTable.Rows[0].Table.Columns.Count - 1;
                            string[] colParams = new string[cnt];
                            for (int col = 0; col < cnt; col++)
                            {
                                var column = dataTable.Rows[0].Table.Columns[col];
                                Console.Out.WriteLine("Column: " + column);
                                prefix += column;

                                string colParam = "@" + column;
                                postfix += colParam;
                                colParams[col] = colParam;
                                
                                if (col != cnt - 1)
                                {
                                    prefix += ", ";
                                    postfix += ", ";
                                }
                                else
                                {
                                    postfix += ")";
                                }
                            }

                            cmd.CommandText = prefix + postfix;
                            cmd.Prepare();

                            int numRows = 0;
                            for (int col = 0; col < dataTable.Rows[0].Table.Columns.Count - 1; col++)
                            {
                                var param = colParams[col];
                                var val = dataTable.Rows[0][col];
                                cmd.Parameters.AddWithValue(param, val);
                            }
                            foreach (DataRow dataRow in dataTable.Rows)
                            {
                                for (int col = 0; col < dataRow.Table.Columns.Count - 1; col++)
                                {
                                    //var param = colParams[col];
                                    //var val = dataRow[col];
                                    //cmd.Parameters.AddWithValue(param, val);
                                    cmd.Parameters[colParams[col]].Value = dataRow[col];
                                }
                                numRows += cmd.ExecuteNonQuery();
                            }

                            //int numRows = cmd.ExecuteNonQuery();
                            if (numRows > 0)
                            {
                                string backupPath = importPath + "\\backup";
                                string backupFilePath = backupPath + "\\" + fileName;
                                if (File.Exists(filePath))
                                {
                                    if (!Directory.Exists(backupPath))
                                    {
                                        Directory.CreateDirectory(backupPath);
                                    }
                                    if (File.Exists(backupFilePath))
                                    {
                                        File.Delete(backupFilePath);
                                    }
                                    File.Move(filePath, backupFilePath); // what if file exists in backup directory???
                                    Console.Out.WriteLine("File moved");
                                    loadFileList();
                                }
                            }
                        }

                        ///TODO - FINISH IMPLIMENTING THIS ??? WHAT'S LEFT TO DO ???
                    }
                    catch (MySql.Data.MySqlClient.MySqlException ex)
                    {
                        string message = "Unable to open MySQL connection - check if the database is installed and running!";
                        Console.Out.WriteLine(message);
                        Console.Out.WriteLine(ex.Message);
                        lblStatus.Text = message;
                    }
                    catch (IOException iox)
                    {
                        string message = "Unable to move file: " + filePath;
                        Console.Out.WriteLine(message);
                        Console.Out.WriteLine(iox.Message);
                        lblStatus.Text = message;
                    }
                    catch (Exception ex)
                    {
                        Console.Out.WriteLine(ex.Message);
                        throw;
                    }
                    finally
                    {
                        if (connection.State == System.Data.ConnectionState.Open)
                        {
                            connection.Close();
                        }
                    }
                }
            }*/
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
