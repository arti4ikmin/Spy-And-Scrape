using System.Net.Mime;
using System.Reflection.Emit;

namespace SpyAndScrape;

using System;
using System.Drawing;
using System.Windows.Forms;

public class InputDialog : Form
{
    private Label labelPrompt;
    private TextBox textBoxInput;
    private Button buttonOk;
    private Button buttonCancel;

    public string InputValue { get; private set; }

    public InputDialog(string title, string prompt, bool isPassword = false)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(400, 120);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(0)));


        labelPrompt = new Label
        {
            Text = prompt,
            Location = new Point(15, 15),
            // Size = new Size(370, 40),
            AutoSize = true,
            MaximumSize = new Size(ClientSize.Width - 30, 0)
        };
        Controls.Add(labelPrompt);

        textBoxInput = new TextBox
        {
            Location = new Point(15, labelPrompt.Bottom + 10),
            Size = new Size(ClientSize.Width - 30, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        if (isPassword)
        {
            textBoxInput.UseSystemPasswordChar = true;
        }
        Controls.Add(textBoxInput);

        buttonOk = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Size = new Size(75, 25),
            Location = new Point(ClientSize.Width - 175, textBoxInput.Bottom + 15)
        };
        buttonOk.Click += (sender, e) => { InputValue = textBoxInput.Text; };
        Controls.Add(buttonOk);

        buttonCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Size = new Size(75, 25),
            Location = new Point(ClientSize.Width - 90, textBoxInput.Bottom + 15)
        };
        Controls.Add(buttonCancel);

        AcceptButton = buttonOk;
        CancelButton = buttonCancel;

        ClientSize = new Size(400, buttonOk.Bottom + 15);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        textBoxInput.Focus();
    }
}