using System;
using System.Drawing;
using System.Windows.Forms;

namespace PeerViewer
{
    public class SettingsForm : Form
    {
        private TextBox _userNameTextBox;
        private Button _okButton;
        private Button _cancelButton;

        public string UserName => _userNameTextBox.Text?.Trim();

        public SettingsForm(string currentUserName)
        {
            InitializeComponent();
            _userNameTextBox.Text = currentUserName ?? string.Empty;
        }

        private void InitializeComponent()
        {
            this.Text = "Settings";
            this.Size = new Size(380, 160);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            var nameLabel = new Label
            {
                Text = "User name:",
                Location = new Point(12, 20),
                Size = new Size(80, 20)
            };

            _userNameTextBox = new TextBox
            {
                Location = new Point(100, 18),
                Size = new Size(250, 22)
            };

            _okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(190, 70),
                Size = new Size(75, 25)
            };

            _cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(275, 70),
                Size = new Size(75, 25)
            };

            this.Controls.AddRange(new Control[] { nameLabel, _userNameTextBox, _okButton, _cancelButton });
            this.AcceptButton = _okButton;
            this.CancelButton = _cancelButton;
        }
    }
}


