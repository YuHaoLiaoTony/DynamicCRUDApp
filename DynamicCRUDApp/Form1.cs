using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DynamicCRUDApp
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            // 1. 初始化視窗基本設定
            this.Text = "動態 CRUD API 工具";
            this.Size = new Size(900, 600);
            this.StartPosition = FormStartPosition.CenterScreen;

            // 2. 建立主要的 TabControl
            tabControl = new TabControl();
            tabControl.Dock = DockStyle.Fill;
            this.Controls.Add(tabControl);

            this.Load += Form1_Load;
        }

        private void Form1_Load_1(object sender, EventArgs e)
        {

        }
    }
}
