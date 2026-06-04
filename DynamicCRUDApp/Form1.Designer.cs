using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DynamicCRUDApp
{
    public partial class Form1 : Form
    {
        private TabControl tabControl;
        private static readonly HttpClient httpClient = new HttpClient();

        int width = 600;
        int height = 800;

        private void Form1_Load(object sender, EventArgs e)
        {
            //InitializeComponent();
            // 確保設定檔存在 (如果沒有，就自動產生一份 Demo 用的)
            if (!File.Exists("AppConfig.json"))
            {
                CreateDummyConfig();
            }

            // 讀取並解析 JSON
            string json = File.ReadAllText("AppConfig.json");
            var configs = JsonSerializer.Deserialize<List<ApiConfig>>(json);

            // 根據設定檔動態產生頁籤與畫面
            foreach (var config in configs)
            {
                BuildTabPage(config);
            }

            tabControl.SelectedIndexChanged += (s, ev) =>
            {
                TriggerCurrentTabRefresh();
            };

            this.Shown += (s, ev) =>
            {
                TriggerCurrentTabRefresh();
            };
        }

        private void BuildTabPage(ApiConfig config)
        {
            TabPage tabPage = new TabPage(config.Name);

            // 使用 TableLayoutPanel 切割上方工具列與下方 Grid
            TableLayoutPanel panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2
            };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // 工具列 (按鈕)
            FlowLayoutPanel toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(5)
            };

            Button btnAdd = new Button { Text = "新增", Height = 30 };
            Button btnRefresh = new Button { Text = "重新整理", Name = "btnRefresh", Height = 30 };

            toolbar.Controls.Add(btnAdd);
            toolbar.Controls.Add(btnRefresh);

            // DataGridView (列表)
            DataGridView dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,

                // ⭐ 關鍵修正 1：關閉自動產生欄位，避免 DataTable 覆蓋或重複產生
                AutoGenerateColumns = false
            };

            // 根據 JSON 中的 Fields 設定 (只顯示 ShowInList 為 true 的)
            foreach (var field in config.Fields.Where(f => f.ShowInList))
            {
                // ⭐ 關鍵修正 2：改用 DataGridViewTextBoxColumn，並明確指定對應的 DataPropertyName

                var col = new DataGridViewTextBoxColumn
                {
                    Name = field.Key,
                    HeaderText = field.Label,
                    DataPropertyName = field.Key,
                };

                dgv.Columns.Add(col);
            }
            // 綁定連線事件 (使用上一個步驟寫好的非同步處理)
            btnRefresh.Click +=  async (s, e) => await ShowRefresh(config, btnRefresh, dgv);
            
            btnAdd.Click += (s, e) => ShowAddDialog(config, () => btnRefresh.PerformClick());

            // 🎯 調整：雙擊事件改為 async，優先呼叫單筆 API
            dgv.CellDoubleClick += ShowEditDialog(config, btnRefresh, dgv);

            // 將控制項加入排版
            panel.Controls.Add(toolbar, 0, 0);
            panel.Controls.Add(dgv, 0, 1);
            tabPage.Controls.Add(panel);

            tabControl.TabPages.Add(tabPage);
        }
        private DataGridViewCellEventHandler ShowEditDialog(ApiConfig config, Button btnRefresh, DataGridView dgv)
        {
            return async (s, e) =>
            {
                if (e.RowIndex < 0) 
                    return;
                DataGridViewRow selectedRow = dgv.Rows[e.RowIndex];

                DataGridViewColumn pkCol = selectedRow.DataGridView.Columns.Cast<DataGridViewColumn>().Where(f => ((dynamic)f.Tag)?.IsPK == true).FirstOrDefault();

                // 準備一個 Dictionary 存放最終要丟給編輯視窗的資料
                Dictionary<string, string> formData = new Dictionary<string, string>();
               
                // 2. 判斷 Config 有沒有設定 Detail API
                if (!config.Apis.ContainsKey("Detail"))
                {
                    // 3. 根本沒有設定 Detail API，直接從 DataGridViewRow 撈資料
                    foreach (var field in config.Fields)
                    {
                        formData[field.Key] = selectedRow.Cells[field.Key]?.Value?.ToString() ?? "";
                    }
                }
                else
                {
                    try
                    {
                        var apiData = await SendApiAsDictAsync(config, "Detail", selectedRow);
                        foreach (var kv in apiData)
                        {
                            formData[kv.Key] = kv.Value;
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"呼叫單筆 API 失敗，將自動轉用列表資料暫代。\n錯誤原因：{ex.Message}", "提示");
                        // 備援方案：API 萬一掛了，還是從 DataGridView 撈資料頂著用
                        foreach (var field in config.Fields)
                        {
                            formData[field.Key] = selectedRow.Cells[field.Key]?.Value?.ToString() ?? "";
                        }
                    }
                }

                // 呼叫修改視窗 (改傳入處理好的 formData 與 idValue)
                ShowEditDialog(config, selectedRow, formData, () => btnRefresh.PerformClick());
            };
        }

        private void ShowEditDialog(ApiConfig config, DataGridViewRow selectedRow, Dictionary<string, string> formData, Action onSuccess)
        {
            using (Form editForm = new Form())
            {
                editForm.Text = $"修改資料 - {config.Name}";
                editForm.Size = new Size(width, height);
                editForm.StartPosition = FormStartPosition.CenterParent;
                editForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                editForm.MaximizeBox = false;
                editForm.MinimizeBox = false;

                FlowLayoutPanel flowPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.TopDown,
                    Padding = new Padding(20),
                    AutoScroll = true
                };

                Dictionary<string, Control> inputControls = GetFormControls(flowPanel, config.Fields, formData, true);

                Button btnDelete = GetDeleteBtn(config, selectedRow, editForm, inputControls);
                Button btnSave = GetSaveBtn(config, selectedRow, editForm, inputControls);
                Panel buttonContainer = new Panel
                {
                    Width = flowPanel.ClientSize.Width - 10, // 減去一點邊距防置折行
                    Height = 45
                };
                buttonContainer.Controls.Add(btnSave);
                buttonContainer.Controls.Add(btnDelete);
                flowPanel.Controls.Add(buttonContainer);
              
                editForm.Controls.Add(flowPanel);

                if (editForm.ShowDialog() == DialogResult.OK)
                {
                    onSuccess?.Invoke();
                }
            }
        }

        private Button GetDeleteBtn(ApiConfig config, DataGridViewRow selectedRow, Form editForm, Dictionary<string, Control> inputControls)
        {
            Button btnDelete = new Button { Text = "刪除", Width = 100, Height = 35, Dock = DockStyle.Right };
            btnDelete.Click += async (ss, ee) =>
            {
                btnDelete.Enabled = false;

                try
                {
                    if (!config.Apis.ContainsKey("Delete"))
                        throw new Exception("Config 中未設定 Delete API 網址！");

                    await SendApiStringAsync(config, "Delete", selectedRow);

                    MessageBox.Show("刪除成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    editForm.DialogResult = DialogResult.OK;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"刪除失敗：\n{ex.Message}", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    btnDelete.Enabled = true;
                }
            };
            return btnDelete;
        }

        private Button GetSaveBtn(ApiConfig config, DataGridViewRow selectedRow, Form editForm, Dictionary<string, Control> inputControls)
        {
            Button btnSave = new Button { Text = "儲存修改", Width = 100, Height = 35, Dock = DockStyle.Left };
            // 2. 儲存點擊事件
            btnSave.Click += async (ss, ee) =>
            {
                btnSave.Enabled = false;
                Dictionary<string, object> payload = GetPayload(config, inputControls);

                try
                {
                    if (!config.Apis.ContainsKey("Update"))
                        throw new Exception("Config 中未設定 Update API 網址！");

                    string jsonBody = JsonSerializer.Serialize(payload);
                    await SendApiStringAsync(config, "Update", selectedRow, jsonBody);

                    MessageBox.Show("修改成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    editForm.DialogResult = DialogResult.OK;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"修改失敗：\n{ex.Message}", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    btnSave.Enabled = true;
                }
            };
            return btnSave;
        }

        private static Dictionary<string, object> GetPayload(ApiConfig config, Dictionary<string, Control> inputControls)
        {
            var payload = new Dictionary<string, object>();
            foreach (var kvp in inputControls)
            {
                var field = config.Fields.FirstOrDefault(f => f.Key == kvp.Key);
                if (field == null) continue;

                // 先拿到 UI 上的原始文字
                string rawValue = "";
                if (kvp.Value is ComboBox cmb) rawValue = cmb.SelectedItem?.ToString() ?? "";
                else if (kvp.Value is TextBox txt) rawValue = txt.Text;

                // 🎯 依據資料型態 (Type) 進行精準轉型
                switch (field.Type.ToLower())
                {
                    case "number":
                        if (int.TryParse(rawValue, out int intVal)) payload.Add(kvp.Key, intVal);
                        else payload.Add(kvp.Key, 0); // 防呆
                        break;

                    case "object": // 處理巢狀 JSON
                        try
                        {
                            using (var doc = JsonDocument.Parse(rawValue))
                            {
                                payload.Add(kvp.Key, doc.RootElement.Clone());
                            }
                        }
                        catch
                        {
                            payload.Add(kvp.Key, new Dictionary<string, string>()); // 格式錯給空物件
                        }
                        break;

                    case "boolean":
                        // 如果以後有布林值欄位也可以直接支援
                        payload.Add(kvp.Key, rawValue.ToLower() == "true");
                        break;

                    default: // "string" 或沒設定，一律當作一般字串
                        payload.Add(kvp.Key, rawValue);
                        break;
                }
            }

            return payload;
        }

        private async Task ShowRefresh(ApiConfig config, Button btnRefresh, DataGridView dgv)
        {
            btnRefresh.Enabled = false;
            string originalText = btnRefresh.Text;
            btnRefresh.Text = "讀取中...";

            try
            {
                string responseStr = await SendApiStringAsync(config, "List");
                DataTable dt = ConvertJsonToDataTable(responseStr, config.Fields);
                dgv.DataSource = dt;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"呼叫 API 發生錯誤：\n{ex.Message}", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnRefresh.Text = originalText;
                btnRefresh.Enabled = true;
            }
        }

        private void ShowAddDialog(ApiConfig config, Action onSuccess)
        {
            using (Form editForm = new Form())
            {
                // 1. 初始化視窗基本設定
                editForm.Text = $"新增資料 - {config.Name}";
                editForm.Size = new Size(width, height);
                editForm.StartPosition = FormStartPosition.CenterParent;
                editForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                editForm.MaximizeBox = false;
                editForm.MinimizeBox = false;

                FlowLayoutPanel flowPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.TopDown,
                    Padding = new Padding(20),
                    AutoScroll = true
                };

                Dictionary<string, Control> inputControls = GetFormControls(flowPanel, config.Fields);

                // 3. 儲存按鈕
                Button btnSave = new Button { Text = "儲存提交", Width = 100, Height = 35, Margin = new Padding(0, 25, 0, 0) };
                flowPanel.Controls.Add(btnSave);

                // 4. 儲存按鈕點擊事件 (非同步)
                btnSave.Click += async (ss, ee) =>
                {
                    btnSave.Enabled = false;

                    
                    Dictionary<string, object> payload = GetPayload(config, inputControls);

                    try
                    {
                        if (!config.Apis.ContainsKey("Create"))
                            throw new Exception("Config 中未設定 Create API 網址！");

                        string jsonBody = JsonSerializer.Serialize(payload);
                        await SendApiStringAsync(config, "Create", jsonBody: jsonBody);
                   
                        MessageBox.Show("新增成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        editForm.DialogResult = DialogResult.OK;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"新增失敗：\n{ex.Message}", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        btnSave.Enabled = true;
                    }
                };

                editForm.Controls.Add(flowPanel);

                // 5. 彈出視窗並處理成功回呼
                if (editForm.ShowDialog() == DialogResult.OK)
                {
                    onSuccess?.Invoke(); // 觸發傳進來的重新整理動作
                }
            }
        }
        /// <summary>
        /// 動態產生表單控制項（同時支援新增與修改模式）
        /// </summary>
        /// <param name="fields">欄位設定清單</param>
        /// <param name="formData">既有的資料（修改模式用），新增模式傳 null 即可</param>
        /// <param name="isEditMode">是否為修改模式（true: 修改, false: 新增）</param>
        public Dictionary<string, Control> GetFormControls(FlowLayoutPanel flowPanel, IEnumerable<FieldInfo> fields, Dictionary<string, string> formData = default, bool isEditMode = false)
        {
            Dictionary<string, Control> inputControls = new Dictionary<string, Control>();
            flowPanel.Controls.Clear();

            foreach (var field in fields)
            {
                // 區分情境：如果是「新增模式」，遇到主鍵或設定為不可編輯的欄位，就直接跳過不顯示 (對應你原本版本2的邏輯)
                if (!isEditMode && (field.IsPK || field.Editable == false))
                {
                    continue;
                }

                // 1. 取得初始值（只有修改模式才需要抓資料，新增模式一律給空字串）
                string currentValue = (isEditMode && formData != null && formData.ContainsKey(field.Key))
                    ? formData[field.Key]
                    : "";

                // 2. 建立 Label
                Label lbl = new Label { Text = field.Label, Width = 340, Margin = new Padding(0, 8, 0, 2) };
                flowPanel.Controls.Add(lbl);

                // 3. 根據 UiType 建立對應的控制項
                Control inputCtrl;

                if (field.UiType == "Select" && field.Options != null)
                {
                    var cmb = new ComboBox { Width = 340, DropDownStyle = ComboBoxStyle.DropDownList };
                    cmb.Items.AddRange(field.Options.ToArray());
                    inputCtrl = cmb;
                }
                else if (field.UiType == "CheckBox")
                {
                    var chk = new CheckBox { Text = field.Label, Width = 340 };
                    chk.Checked = (currentValue == "true" || currentValue == "1");
                    inputCtrl = chk;
                }
                else if (field.UiType == "TextArea")
                {
                    inputCtrl = new TextBox
                    {
                        Width = 340,
                        Height = 100,
                        Multiline = true,
                        ScrollBars = ScrollBars.Vertical,
                        WordWrap = true
                    };
                }
                else
                {
                    inputCtrl = new TextBox { Width = 340 };
                }

                // 4. 處理「修改模式」下的唯讀狀態 (對應你原本版本1的邏輯)
                if (isEditMode && (field.IsPK || field.Editable == false))
                {
                    if (inputCtrl is TextBox txt)
                    {
                        txt.ReadOnly = true; // 用 ReadOnly 替代 Enabled = false，滑鼠還可以選取複製，UX 較佳
                    }
                    else
                    {
                        inputCtrl.Enabled = false;
                    }
                }

                // 5. 填入數值（非 CheckBox 的控制項）
                if (field.UiType != "CheckBox")
                {
                    inputCtrl.Text = currentValue;
                }

                // 6. 加入畫面與快取字典
                flowPanel.Controls.Add(inputCtrl);
                inputControls.Add(field.Key, inputCtrl);
            }
            return inputControls;
        }
        private DataTable ConvertJsonToDataTable(string json, List<FieldInfo> fields)
        {
            DataTable dt = new DataTable();

            // 根據 Config 的 Fields 定義 DataTable 的欄位
            foreach (var field in fields)
            {
                dt.Columns.Add(field.Key, typeof(string)); // 簡單起見，欄位先全以 string 處理
            }

            // 解析 JSON
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                // 確保回傳根節點是陣列 (例如: [ {}, {}, {} ])
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement element in doc.RootElement.EnumerateArray())
                    {
                        DataRow row = dt.NewRow();

                        // 依據 Config 設定的 Key 去對應 JSON 內容
                        foreach (var field in fields)
                        {
                            if (element.TryGetProperty(field.Key, out JsonElement prop))
                            {
                                row[field.Key] = prop.ToString();
                            }
                            else
                            {
                                row[field.Key] = DBNull.Value; // API 沒回傳該欄位時填空
                            }
                        }
                        dt.Rows.Add(row);
                    }
                }
            }

            return dt;
        }
        private void CreateDummyConfig()
        {
            // 產生一份測試用的 JSON 設定檔
            var dummyJson = @"[
              {
                ""Id"": ""mock_api_01"",
                ""Name"": ""使用者管理"",
                ""BaseUrl"": ""https://api.example.com/v1"",
                ""Apis"": {
                  ""List"": { ""Url"": ""/users"", ""Method"": ""GET"" },
                  ""Create"": { ""Url"": ""/users"", ""Method"": ""POST"" }
                },
                ""Fields"": [
                  { ""Key"": ""id"", ""Label"": ""流水號"", ""Type"": ""String"", ""ShowInList"": true },
                  { ""Key"": ""username"", ""Label"": ""帳號"", ""Type"": ""String"", ""ShowInList"": true },
                  { ""Key"": ""role"", ""Label"": ""角色"", ""Type"": ""Select"", ""ShowInList"": true }
                ]
              },
              {
                ""Id"": ""mock_api_02"",
                ""Name"": ""商品維護"",
                ""BaseUrl"": ""https://api.example.com/v1"",
                ""Apis"": {
                  ""List"": { ""Url"": ""/products"", ""Method"": ""GET"" }
                },
                ""Fields"": [
                  { ""Key"": ""prodId"", ""Label"": ""商品編號"", ""Type"": ""String"", ""ShowInList"": true },
                  { ""Key"": ""price"", ""Label"": ""價格"", ""Type"": ""Number"", ""ShowInList"": true }
                ]
              }
            ]";
            File.WriteAllText("AppConfig.json", dummyJson);
        }

        private void TriggerCurrentTabRefresh()
        {
            // 如果連一個頁籤都沒有，就不處理
            if (tabControl.SelectedTab == null) return;

            // 🔍 核心妙招：從「當前顯示的頁籤」裡面，往下挖出名叫 "btnRefresh" 的那顆按鈕
            var btn = tabControl.SelectedTab.Controls.Find("btnRefresh", true).FirstOrDefault() as Button;

            // 只要找到了，而且這時候頁面已經看得到了（Visible），PerformClick 就能100%安全執行！
            btn?.PerformClick();
        }

        public async Task<Dictionary<string, string>> SendApiAsDictAsync(ApiConfig config, string action, DataGridViewRow selectedRow = null, string jsonBody = null)
        {
            string jsonResult = await SendApiStringAsync(config, action, selectedRow, jsonBody);

            Dictionary<string, string> formData = new Dictionary<string, string>();
            // 解析單筆 JSON 物件並塞入 formData
            using (JsonDocument doc = JsonDocument.Parse(jsonResult))
            {
                foreach (var field in config.Fields)
                {
                    if (doc.RootElement.TryGetProperty(field.Key, out JsonElement prop))
                    {
                        formData[field.Key] = prop.ToString();
                    }
                }
            }

            return formData;
        }
        private async Task<string> SendApiStringAsync(ApiConfig config, string action, DataGridViewRow selectedRow = null, string jsonBody = null)
        {
            var apiConfig = config.Apis.ContainsKey(action) ? config.Apis[action] : null;
            string apiUrl = $"{config.BaseUrl.TrimEnd('/')}{apiConfig.Url}";

            if(selectedRow != null)
            {
                foreach (var item in selectedRow.DataGridView.Columns.Cast<DataGridViewColumn>())
                {
                    apiUrl = apiUrl.Replace($"{{{item.Name}}}", selectedRow.Cells[item.Name].Value?.ToString() ?? string.Empty);
                }
            }

            string method = apiConfig.Method.ToUpper();

            var request = new HttpRequestMessage(new HttpMethod(method), apiUrl);

            if (jsonBody != null)
            {
                var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
                request.Content = content;
            }

            if (config.Headers != null)
            {
                foreach (var item in config.Headers)
                {
                    request.Headers.Add(item.Key, item.Value);
                }
            }

            if (apiConfig.Headers != null)
            {
                foreach (var item in apiConfig.Headers)
                {
                    request.Headers.Add(item.Key, item.Value);
                }
            }

            using (var response = await httpClient.SendAsync(request))
            {
                response.EnsureSuccessStatusCode();

                string jsonResult = await response.Content.ReadAsStringAsync();
                return jsonResult;
            }
        }
    }
}

