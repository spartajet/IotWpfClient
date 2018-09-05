using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows;
using MQTTnet;
using MQTTnet.Client;
using OxyPlot;

namespace IotWpfClient
{
    /// <inheritdoc cref="Window" />
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// force数据
        /// </summary>
        public IList<DataPoint> ForceDataPoints { get; set; } = new List<DataPoint>();

        /// <summary>
        /// force ConcurrentQueue
        /// </summary>
        private ConcurrentQueue<double> _forceConcurrentQueue = new ConcurrentQueue<double>();

        /// <summary>
        /// forceBroadcastBlock
        /// </summary>
        private BroadcastBlock<double> _forceBroadcastBlock = new BroadcastBlock<double>(t => t);

        /// <summary>
        /// forceUI更新ActionBlock
        /// </summary>
        private ActionBlock<double> _forceUiActionBlock;

        /// <summary>
        /// force数据上传ActionBlock
        /// </summary>
        private ActionBlock<double> _forceUploadActionBlock;

        /// <summary>
        /// 每个X的索引
        /// </summary>
        private int _xIndex;

        private Random _random = new Random();

        /// <summary>
        /// 生成数据标记
        /// </summary>
        private bool isStartGenerateData;

        /// <summary>
        /// 测量控制标记
        /// </summary>
        private bool isStartMeasure;

        /// <summary>
        /// 是否上传数据
        /// </summary>
        public bool IsUploadData { get; set; } = true;

        /// <summary>
        /// MQTT 客户端
        /// </summary>
        private IMqttClient _mqttClient;


        public MainWindow()
        {
            this.InitializeComponent();
            this.Loaded += this.MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            this._forceUiActionBlock = new ActionBlock<double>(t =>
            {
                if (this.ForceDataPoints.Count >= 500)
                {
                    this.ForceDataPoints.RemoveAt(0);
                }

                this.ForceDataPoints.Add(new DataPoint(this._xIndex++, t));
                Application.Current.Dispatcher.Invoke(() => { this.ForcePlot?.InvalidatePlot(); });
            }, new ExecutionDataflowBlockOptions()
            {
                TaskScheduler = TaskScheduler.FromCurrentSynchronizationContext()
            });
            this._forceUploadActionBlock = new ActionBlock<double>( t =>
            {
                if (!this.IsUploadData)
                {
                    return;
                }
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic("measure/force")
                    .WithPayload(t.ToString(CultureInfo.InvariantCulture))
                    .WithExactlyOnceQoS()
                    .WithRetainFlag()
                    .Build();

                 this._mqttClient.PublishAsync(message);
            });
            this.LinkBlocks();
            if (this.IsUploadData)
            {
                this.InitialMqtt();
            }
        }

        /// <summary>
        /// 初始化初始化MQTT
        /// </summary>
        private void InitialMqtt()
        {
            this._mqttClient = new MqttFactory().CreateMqttClient();
            this._mqttClient.ConnectAsync(new MqttClientOptionsBuilder()
                .WithClientId(Guid.NewGuid().ToString("N"))
                .WithTcpServer("*****",1883)
                .WithCredentials("admin", "admin")
//                .WithTls()
                .WithCleanSession()
                .Build());
        }

        /// <summary>
        /// 链接各种block
        /// </summary>
        private void LinkBlocks()
        {
            this._forceBroadcastBlock.LinkTo(this._forceUploadActionBlock);
            this._forceBroadcastBlock.LinkTo(this._forceUiActionBlock);
           
        }

        /// <summary>
        /// 生成数据事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void GenerateDataButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (this.GenerateDataButton.Content.Equals("生成数据"))
            {
                this.isStartGenerateData = true;
                Task.Factory.StartNew(async () =>
                {
                    while (this.isStartGenerateData)
                    {
                        this._forceConcurrentQueue.Enqueue(this._random.NextDouble() * 10);
                        await Task.Delay(TimeSpan.FromMilliseconds(50));
                    }
                });
                this.GenerateDataButton.Content = "停止生成";
            }
            else
            {
                this.isStartGenerateData = false;
                this.GenerateDataButton.Content = "生成数据";
            }
        }

        /// <summary>
        /// 测量控制按钮事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MeasureControlButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (this.MeasureControlButton.Content.Equals("开始测量"))
            {
                this.ForceDataPoints.Clear();
                this._xIndex = 0;
                this.isStartMeasure = true;
                Task.Factory.StartNew(async () =>
                {
                    while (this.isStartMeasure)
                    {
                        if (this._forceConcurrentQueue.Count > 0)
                        {
                            this._forceConcurrentQueue.TryDequeue(out var result);
                            this._forceBroadcastBlock.Post(result);
                            await Task.Delay(TimeSpan.FromMilliseconds(20));
                        }
                        else
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(50));
                        }
                    }
                });
                this.MeasureControlButton.Content = "结束测量";
            }
            else
            {
                this.isStartMeasure = false;
                this.MeasureControlButton.Content = "开始测量";
            }
        }
    }
}