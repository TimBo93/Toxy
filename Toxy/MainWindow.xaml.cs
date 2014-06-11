﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using MahApps.Metro.Controls;
using SharpTox;
using System.Threading;

namespace Toxy
{
    class FileTransfer
    {
        public int FriendNumber { get; set; }
        public int FileNumber { get; set; }
        public ulong FileSize { get; set; }
        public ProgressBar ProgressBar { get; set; }
        public Label StatusLabel { get; set; }
        public string FileName { get; set; }
        public Stream Stream { get; set; }
        public Button CancelButton { get; set; }
        //public bool Finishe
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        private Tox tox;
        private Dictionary<int, FlowDocument> convdic = new Dictionary<int, FlowDocument>();
        private List<FileTransfer> transfers = new List<FileTransfer>();

        private int current_number = 0;
        private bool resizing = false;
        private bool focusTextbox = false;
        private bool typing = false;

        public MainWindow()
        {
            InitializeComponent();

            tox = new Tox(false);
            tox.Invoker = Dispatcher.BeginInvoke;
            tox.OnNameChange += tox_OnNameChange;
            tox.OnFriendMessage += tox_OnFriendMessage;
            tox.OnFriendAction += tox_OnFriendAction;
            tox.OnUserStatus += tox_OnUserStatus;
            tox.OnStatusMessage += tox_OnStatusMessage;
            tox.OnTypingChange += tox_OnTypingChange;
            tox.OnConnectionStatusChanged += tox_OnConnectionStatusChanged;
            tox.OnFileSendRequest += tox_OnFileSendRequest;
            tox.OnFileData += tox_OnFileData;
            tox.OnFileControl += tox_OnFileControl;

            bool bootstrap_success = false;
            foreach (ToxNode node in nodes)
            {
                if (tox.BootstrapFromNode(node))
                    bootstrap_success = true;
            }

            if (File.Exists("data"))
            {
                if (!tox.Load("data"))
                {
                    MessageBox.Show("Could not load tox data, this program will now exit.", "Error");
                    Close();
                }
            }

            tox.Start();

            if (string.IsNullOrEmpty(tox.GetSelfName()))
                tox.SetName("Toxy User");

            Username.Text = tox.GetSelfName();

            Userstatus.Text = tox.GetSelfStatusMessage();
            Status.SelectedIndex = 0;

            InitFriends();
            if (tox.GetFriendlistCount() > 0)
                SelectFriendControl(GetFriendControlByNumber(0));
        }

        private void tox_OnFileControl(int friendnumber, int receive_send, int filenumber, int control_type, byte[] data)
        {
            switch((ToxFileControl)control_type)
            {
                case ToxFileControl.FINISHED:
                    {
                        FileTransfer ft = GetFileTransfer(friendnumber, filenumber);

                        if (ft == null)
                            return;

                        ft.Stream.Close();
                        ft.Stream = null;

                        ft.CancelButton.Visibility = Visibility.Hidden;
                        ft.StatusLabel.Content = "Finished";

                        transfers.Remove(ft);

                        break;
                    }
            }
        }

        private FileTransfer GetFileTransfer(int friendnumber, int filenumber)
        {
            foreach (FileTransfer ft in transfers)
                if (ft.FileNumber == filenumber && ft.FriendNumber == friendnumber)
                    return ft;

            return null;
        }

        private void tox_OnFileData(int friendnumber, int filenumber, byte[] data)
        {
            FileTransfer ft = GetFileTransfer(friendnumber, filenumber);

            if (ft == null)
                return;

            if (ft.Stream == null)
                throw new Exception("Unexpectedly received data");

            ulong remaining = tox.FileDataRemaining(friendnumber, filenumber, 1);
            double value = (double)remaining / (double)ft.FileSize;

            ft.ProgressBar.Value = (100 - (int)(value * 100));
            ft.StatusLabel.Content = string.Format("{0}/{1}", ft.FileSize - remaining, ft.FileSize);

            ft.Stream.Write(data, 0, data.Length);
        }

        private void tox_OnFileSendRequest(int friendnumber, int filenumber, ulong filesiz, string filename)
        {
            if (!convdic.ContainsKey(friendnumber))
                convdic.Add(friendnumber, GetNewFlowDocument());

            FileTransfer transfer = AddNewFTRowToDocument(convdic[friendnumber], friendnumber, filenumber, filename, filesiz);
            transfers.Add(transfer);

            tox.FileSendControl(friendnumber, 1, filenumber, (int)ToxFileControl.ACCEPT, new byte[0]);
            transfer.Stream = new FileStream(filename, FileMode.Create);
        }

        private void tox_OnConnectionStatusChanged(int friendnumber, byte status)
        {
            if (status == 0)
            {
                DateTime lastOnline = tox.GetLastOnline(friendnumber);
                FriendControl control = GetFriendControlByNumber(friendnumber);

                if (control == null)
                    return;

                control.SetStatusMessage("Last seen: " + lastOnline.ToShortDateString() + " " + lastOnline.ToLongTimeString());
                control.SetStatus(ToxUserStatus.INVALID); //not the proper way to do it, I know...
            }
        }

        private void tox_OnTypingChange(int friendnumber, bool is_typing)
        {
            if (current_number == friendnumber)
            {
                if (is_typing)
                    TypingStatusLabel.Content = tox.GetName(friendnumber) + " is typing...";
                else
                    TypingStatusLabel.Content = "";
            }
        }

        private void tox_OnFriendAction(int friendnumber, string action)
        {
            MessageData data = new MessageData() { Username = "*", Message = string.Format("{0} {1}", tox.GetName(friendnumber), action) };

            if (convdic.ContainsKey(friendnumber))
                AddNewRowToDocument(convdic[friendnumber], data);
            else
            {
                FlowDocument document = GetNewFlowDocument();
                convdic.Add(friendnumber, document);
                AddNewRowToDocument(convdic[friendnumber], data);
            }

            //if (current_number == friendnumber)
              //  ChatBox.

            this.Flash();
        }

        private FlowDocument GetNewFlowDocument()
        {
            Stream doc_stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Toxy.Message.xaml");
            FlowDocument doc = (FlowDocument)XamlReader.Load(doc_stream);
            doc.IsEnabled = true;

            return doc;
        }

        private void tox_OnFriendMessage(int friendnumber, string message)
        {
            MessageData data = new MessageData() { Username = tox.GetName(friendnumber), Message = message };

            if (convdic.ContainsKey(friendnumber))
                AddNewRowToDocument(convdic[friendnumber], data);
            else
            {
                FlowDocument document = GetNewFlowDocument();
                convdic.Add(friendnumber, document);
                AddNewRowToDocument(convdic[friendnumber], data);
            }

            //if (current_number == friendnumber)
              //  ChatBox.ScrollToEnd();

            this.Flash();
        }

        private FileTransfer AddNewFTRowToDocument(FlowDocument doc, int friendnumber, int filenumber, string filename, ulong filesize)
        {
            ProgressBar bar = new ProgressBar() { Value = 0, Width = 100 };
            Label label = new Label() { Content = "0/" + filesize.ToString() };
            Button button = new Button() { Content = "Cancel", Width = 75 };
            FileTransfer transfer = new FileTransfer() { FriendNumber = friendnumber, FileNumber = filenumber, FileName = filename, FileSize = filesize, StatusLabel = label, ProgressBar = bar, CancelButton = button };

            TableRow newTableRow = new TableRow();
            InlineUIContainer fileTransferContainer = new InlineUIContainer();
            FileTransferControl fileTransferControl = new FileTransferControl();
            fileTransferContainer.Child = fileTransferControl;
            

            Paragraph usernameParagraph = new Paragraph();
            usernameParagraph.Inlines.Add(fileTransferContainer);

            TableCell fileTableCell = new TableCell();
            fileTableCell.ColumnSpan = 2;
            fileTableCell.Blocks.Add(usernameParagraph);

            newTableRow.Cells.Add(fileTableCell);

            //TableCell usernameTableCell = new TableCell();
            //Paragraph usernameParagraph = new Paragraph();

            //usernameParagraph.Inlines.Add(tox.GetName(friendnumber));
            //usernameTableCell.Blocks.Add(usernameParagraph);

            //TableCell messageTableCell = new TableCell();
            //Paragraph messageParagraph = new Paragraph();

            //messageParagraph.Inlines.Add("Transferring " + filename);

            //messageParagraph.Inlines.Add(label);
            //messageParagraph.Inlines.Add("\n");
            //messageParagraph.Inlines.Add(bar);
            //messageParagraph.Inlines.Add("  ");
            //messageParagraph.Inlines.Add(button);

            //messageTableCell.Blocks.Add(messageParagraph);

            //newTableRow.Cells.Add(usernameTableCell);
            //newTableRow.Cells.Add(messageTableCell);

            TableRowGroup MessageRows = (TableRowGroup)doc.FindName("MessageRows");
            MessageRows.Rows.Add(newTableRow);

            ChatBox.Document = doc;

            return transfer;
        }

        private void AddNewRowToDocument(FlowDocument doc, MessageData data)
        {
            doc.IsEnabled = true;

            //Make a new row
            TableRow newTableRow = new TableRow();

            //Make a new cell and create a paragraph in it
            TableCell usernameTableCell = new TableCell();
            Paragraph usernameParagraph = new Paragraph();

            usernameParagraph.Inlines.Add(data.Username);
            usernameTableCell.Blocks.Add(usernameParagraph);

            //Make a new cell and create a paragraph in it
            TableCell messageTableCell = new TableCell();
            Paragraph messageParagraph = new Paragraph();

            messageParagraph.Inlines.Add(data.Message);

            //messageParagraph.Inlines.Add(fakeHyperlink);
            messageTableCell.Blocks.Add(messageParagraph);

            //Add the two cells to the row we made before
            newTableRow.Cells.Add(usernameTableCell);
            newTableRow.Cells.Add(messageTableCell);

            //Adds row to the Table > TableRowGroup
            TableRowGroup MessageRows = (TableRowGroup)doc.FindName("MessageRows");
            MessageRows.Rows.Add(newTableRow);

            ChatBox.Document = doc;
        }

        private FriendControl GetFriendControlByNumber(int friendnumber)
        {
            foreach (FriendControl control in FriendWrapper.FindChildren<FriendControl>())
                if (control.FriendNumber == friendnumber)
                    return control;

            return null;
        }

        private void tox_OnUserStatus(int friendnumber, ToxUserStatus status)
        {
            FriendControl control = GetFriendControlByNumber(friendnumber);
            control.SetStatus(status);
        }

        private void tox_OnStatusMessage(int friendnumber, string newstatus)
        {
            FriendControl control = GetFriendControlByNumber(friendnumber);
            control.SetStatusMessage(newstatus);
        }

        private void tox_OnNameChange(int friendnumber, string newname)
        {
            FriendControl control = GetFriendControlByNumber(friendnumber);
            control.SetUsername(newname);
        }

        private ToxNode[] nodes = new ToxNode[] { 
            new ToxNode("192.254.75.98", 33445, "951C88B7E75C867418ACDB5D273821372BB5BD652740BCDF623A4FA293E75D2F", false),
            new ToxNode("37.187.46.132", 33445, "A9D98212B3F972BD11DA52BEB0658C326FCCC1BFD49F347F9C2D3D8B61E1B927", false),
            new ToxNode("54.199.139.199", 33445, "7F9C31FE850E97CEFD4C4591DF93FC757C7C12549DDD55F8EEAECC34FE76C029", false) 
        };

        /// <summary>
        /// Loops through the received friendlist and calls AddFriendToView
        /// </summary>
        private void InitFriends()
        {
            foreach (int FriendNumber in tox.GetFriendlist())
            {
                //Creates a new FriendControl for every friend
                AddFriendToView(FriendNumber);
            }
        }

        /// <summary>
        /// Adds the specific friend to the FriendWrapper
        /// </summary>
        /// <param name="friendNumber">Specific friendnumber</param>
        private void AddFriendToView(int friendNumber)
        {
            string friendName = tox.GetName(friendNumber);
            string friendStatus;

            if (tox.GetFriendConnectionStatus(friendNumber) != 0)
            {
                friendStatus = tox.GetStatusMessage(friendNumber);
            }
            else
            {
                DateTime lastOnline = tox.GetLastOnline(friendNumber);
                friendStatus = "Last seen: " + lastOnline.ToShortDateString() + " " + lastOnline.ToLongTimeString();
            }

            if (string.IsNullOrEmpty(friendName))
                friendName = tox.GetClientID(friendNumber);

            FriendControl friend = new FriendControl(friendNumber);
            friend.FriendNameLabel.Content = friendName;
            friend.FriendStatusLabel.Content = friendStatus;
            friend.Click += friend_Click;
            FriendWrapper.Children.Add(friend);
        }

        private void SelectFriendControl(FriendControl friend)
        {
            Grid grid = (Grid)friend.FindName("FriendGrid");
            friend.Selected = true;
            grid.Background = new SolidColorBrush(Color.FromRgb(236, 236, 236));

            int friendNumber = friend.FriendNumber;

            foreach (FriendControl control in FriendWrapper.FindChildren<FriendControl>())
            {
                if (friend != control)
                {
                    Grid grid1 = (Grid)control.FindName("FriendGrid");
                    control.Selected = false;
                    grid1.Background = new SolidColorBrush(Colors.White);
                }
            }

            Friendname.Text = tox.GetName(friendNumber);
            Friendstatus.Text = tox.GetStatusMessage(friendNumber);
            current_number = friend.FriendNumber;

            if (convdic.ContainsKey(current_number))
            {
                ChatBox.Document = convdic[current_number];
                //ChatBox.ScrollToEnd();
            }
            else
            {
                FlowDocument document = GetNewFlowDocument();
                convdic.Add(current_number, document);
                ChatBox.Document = convdic[current_number];
            }
        }

        private void friend_Click(object sender, RoutedEventArgs e)
        {
            FriendControl control = (FriendControl)sender;

            if (!tox.GetIsTyping(control.FriendNumber))
                TypingStatusLabel.Content = "";
            else
                TypingStatusLabel.Content = tox.GetName(control.FriendNumber) + " is typing...";

            SelectFriendControl(control);
        }

        private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            tox.Save("data");
            tox.Kill();
        }

        private void OpenAddFriend_Click(object sender, RoutedEventArgs e)
        {
            FriendFlyout.IsOpen = !FriendFlyout.IsOpen;
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsUsername.Text = tox.GetSelfName();
            SettingsStatus.Text = tox.GetSelfStatusMessage();
            SettingsFlyout.IsOpen = !SettingsFlyout.IsOpen;
        }
        private void AddFriend_Click(object sender, RoutedEventArgs e)
        {
            TextRange message = new TextRange(AddFriendMessage.Document.ContentStart, AddFriendMessage.Document.ContentEnd);

            if (!(!string.IsNullOrEmpty(AddFriendID.Text) && !string.IsNullOrEmpty(message.Text)))
                return;

            if (AddFriendID.Text.Contains("@"))
            {
                try
                {
                    string id = DnsTools.DiscoverToxID(AddFriendID.Text);
                    AddFriendID.Text = id;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not find a tox id:\n" + ex.ToString());
                }

                return;
            }

            int friendnumber;
            try
            {
                friendnumber = tox.AddFriend(AddFriendID.Text, message.Text);
                FriendFlyout.IsOpen = false;
                AddFriendToView(friendnumber);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private void Status_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            tox.SetUserStatus((ToxUserStatus)Status.SelectedIndex);
        }

        private void SaveSettingsButton_OnClick(object sender, RoutedEventArgs e)
        {
            tox.SetName(SettingsUsername.Text);
            tox.SetStatusMessage(SettingsStatus.Text);

            Username.Text = SettingsUsername.Text;
            Userstatus.Text = SettingsStatus.Text;

            SettingsFlyout.IsOpen = false;
        }

        private void TextToSend_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (Keyboard.IsKeyDown(Key.LeftShift))
                {
                    TextToSend.Text += Environment.NewLine;
                    TextToSend.CaretIndex = TextToSend.Text.Length;
                    return;
                }

                if (e.IsRepeat)
                    return;

                string text = TextToSend.Text;

                if (string.IsNullOrEmpty(text))
                    return;

                if (tox.GetFriendConnectionStatus(current_number) == 0)
                    return;

                if (text.StartsWith("/me "))
                {
                    //action
                    string action = text.Substring(4);
                    int messageid = tox.SendAction(current_number, action);

                    MessageData data = new MessageData() { Username = "*", Message = string.Format("{0} {1}", tox.GetSelfName(), action) };

                    if (convdic.ContainsKey(current_number))
                    {
                        AddNewRowToDocument(convdic[current_number], data);
                    }
                    else
                    {
                        FlowDocument document = GetNewFlowDocument();
                        convdic.Add(current_number, document);
                        AddNewRowToDocument(convdic[current_number], data);
                    }

                    //ChatBox.ScrollToEnd();
                }
                else
                {
                    //regular message
                    string message = text;
                    int messageid = tox.SendMessage(current_number, message);

                    MessageData data = new MessageData() { Username = tox.GetSelfName(), Message = message };

                    if (convdic.ContainsKey(current_number))
                    {
                        AddNewRowToDocument(convdic[current_number], data);
                    }
                    else
                    {
                        FlowDocument document = GetNewFlowDocument();
                        convdic.Add(current_number, document);
                        AddNewRowToDocument(convdic[current_number], data);
                    }

                    //ChatBox.ScrollToEnd();
                }

                TextToSend.Text = "";
                e.Handled = true;
            }
        }

        private void GithubButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("https://github.com/Reverp/Toxy-WPF");
        }

        private void TextToSend_TextChanged(object sender, TextChangedEventArgs e)
        {
            string text = TextToSend.Text;

            if (string.IsNullOrEmpty(text))
            {
                if (typing)
                {
                    typing = false;
                    tox.SetUserIsTyping(current_number, typing);
                }
            }
            else
            {
                if (!typing)
                {
                    typing = true;
                    tox.SetUserIsTyping(current_number, typing);
                }
            }
        }

        private void CopyIDButton_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(tox.GetAddress());
        }

        private void MetroWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!resizing && focusTextbox)
                TextToSend.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
        }

        private void TextToSend_OnGotFocus(object sender, RoutedEventArgs e)
        {
            focusTextbox = true;
        }

        private void TextToSend_OnLostFocus(object sender, RoutedEventArgs e)
        {
            focusTextbox = false;
        }

        private void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (resizing)
            {
                resizing = false;
                if (focusTextbox)
                {
                    TextToSend.Focus();
                    focusTextbox = false;
                }
            }
        }

        private void ChatDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            e.Cancel = true;
        }

        private void OnlineThumbButton_Click(object sender, EventArgs e)
        {
            tox.SetUserStatus(ToxUserStatus.NONE);
        }

        private void AwayThumbButton_Click(object sender, EventArgs e)
        {
            tox.SetUserStatus(ToxUserStatus.AWAY);
        }

        private void BusyThumbButton_Click(object sender, EventArgs e)
        {
            tox.SetUserStatus(ToxUserStatus.BUSY);
        }
    }

    public class MessageData
    {
        public string Username { get; set; }
        public string Message { get; set; }
    }
}