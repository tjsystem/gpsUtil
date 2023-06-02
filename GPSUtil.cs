using System;
using System.IO;
using System.IO.Ports;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading;
using System.Globalization;
using System.Windows.Forms;

namespace GPSUtil
{
	/// <summary>
	/// コンボボックス等にセットするアイテム用
	/// </summary>
	public class FormItem
	{
		private int m_id;
		private string m_name;

		public FormItem()
		{
			m_id = -1;
			m_name = "";
		}

		public FormItem(int id, string name)
		{
			m_id = id;
			m_name = name;
		}

		public int Id
		{
			get { return m_id; }
			set { m_id = value; }
		}

		public string Name
		{
			get { return m_name; }
			set { m_name = value; }
		}

		public override string ToString()
		{
			return m_name;
		}
	}


	/// <summary>
	/// GPSとのシリアルポート接続
	/// </summary>
	public class GpsConnector
	{
		/// <summary>
		/// シリアルポートインスタンス
		/// </summary>
		private SerialPort m_port;

		/// <summary>
		/// シリアルポートとの通信用スレッド
		/// </summary>
		private Thread m_threadOperatePort;

		/// <summary>
		/// 通信用スレッド終了用フラグ
		/// </summary>
		private bool m_abortThread;

		/// <summary>
		/// シリアルポート通信時の排他用（主にバッファアクセスの排他）
		/// </summary>
		private object m_lockOperatePort = new object();

		/// <summary>
		/// 受信用バッファの最大サイズ
		/// </summary>
		protected int m_maxRecvBuffer;
		/// <summary>
		/// 受信用バッファの最大サイズ
		/// </summary>
		public int MaxRecvBuffer
		{
			get { return m_maxRecvBuffer; }
			set { m_maxRecvBuffer = value; }
		}

		/// <summary>
		/// コマンド送信ログ
		/// </summary>
		protected List<string> m_lstBufferCmdSend;

		/// <summary>
		/// コマンド送信ログの未取得行の開始インデックス
		/// </summary>
		protected int m_unacquiredIndexBufferCmdSend;

		/// <summary>
		/// コマンド結果受信用バッファ
		/// </summary>
		protected List<string> m_lstBufferCmdResult;

		/// <summary>
		/// 受信用バッファ
		/// </summary>
		protected List<string> m_lstBufferRecv;


		/// <summary>
		/// コンストラクタ
		/// </summary>
		public GpsConnector()
		{
			// シリアルポートを用意
			m_port = new SerialPort();
			m_port.NewLine = "\r\n";
			m_port.BaudRate = 115200;
			m_port.Handshake = Handshake.XOnXOff;
			m_port.Parity = Parity.Odd;

			// バッファを準備
			m_lstBufferCmdSend = new List<string>();
			m_unacquiredIndexBufferCmdSend = 0;
			m_lstBufferCmdResult = new List<string>();
			m_lstBufferRecv = new List<string>();
			m_maxRecvBuffer = 1000;

			// 通信用スレッド
			m_threadOperatePort = null;
			m_abortThread = false;
		}

		/// <summary>
		/// デストラクタ
		/// </summary>
		~GpsConnector()
		{
			Close();
		}

		/// <summary>
		/// 終了処理。 使用終了時に呼び出してください。
		/// </summary>
		public void Close()
		{
			// シリアルポートの後始末
			disConnect();

			// スレッド停止
			if (m_threadOperatePort != null)
			{
				m_abortThread = true;   // 終了指示
			}
		}

		// シリアルポート設定取得プロパティ
		public string PortName
		{
			get { return m_port.PortName; }
		}
		public int BauRate
		{
			get { return m_port.BaudRate; }
		}
		public Handshake Handshake
		{
			get { return m_port.Handshake; }
		}
		public Parity Parity
		{
			get { return m_port.Parity; }
		}

		// シリアルポート設定コンボボックス用アイテム取得
		public static int[] getBauRateItems()
		{
			int[] items = new int[] { 9600, 19200, 28800, 38400, 57600, 115200 };
			return items;
		}
		public static FormItem[] getHandshakeItems()
		{
			FormItem[] items = new FormItem[]
			{
				new FormItem((int)Handshake.None, "なし" ),
				new FormItem((int)Handshake.XOnXOff, "XON/XOFF" ),
				new FormItem((int)Handshake.RequestToSend, "RTS/CTS" ),
				new FormItem((int)Handshake.RequestToSendXOnXOff, "XON/XOFF + RTS/CTS" )
			};
			return items;
		}
		public static FormItem[] getParityItems()
		{
			FormItem[] items = new FormItem[]
			{
				new FormItem((int)Parity.None, "なし" ),
				new FormItem((int)Parity.Even, "偶数" ),
				new FormItem((int)Parity.Odd, "奇数" ),
				new FormItem((int)Parity.Mark, "常に1" ),
				new FormItem((int)Parity.Space, "常に0" )
			};
			return items;
		}

		/// <summary>
		/// GPS接続状態
		/// </summary>
		/// <returns>接続中ならばtrueを返す</returns>
		public bool IsConnected
		{
			get { return m_port.IsOpen; }
		}

		/// <summary>
		/// GPSに接続
		/// </summary>
		/// <returns></returns>
		public bool connect(string portName, int bauRate = 115200, int handShake = (int)Handshake.XOnXOff, int parity = (int)Parity.Odd)
		{
			if (m_port.IsOpen)
				return false;   // オープン済のためエラー

			try
			{
				// ポートオープン
				m_port.PortName = portName;
				m_port.BaudRate = bauRate;
				m_port.Handshake = (Handshake)Enum.ToObject(typeof(Handshake), handShake);
				m_port.Parity = (Parity)Enum.ToObject(typeof(Parity), parity);
				m_port.Open();

				if (m_port.IsOpen)
				{
					// 受信用スレッドを生成
					if (m_threadOperatePort == null)
					{
						m_threadOperatePort = new Thread(funcOperatePort);
						m_threadOperatePort.Start();
					}
				}
			}
			catch (Exception)
			{
				return false;
			}
			return true;
		}

		/// <summary>
		/// GPSとの接続解除
		/// </summary>
		public void disConnect()
		{
			if (m_port.IsOpen)
			{
				try
				{
					m_port.Close();
				}
				catch (Exception)
				{
					// NOP
				}
			}
		}

		/// <summary>
		/// シリアルポートとの通信スレッド用関数
		/// </summary>
		private void funcOperatePort()
		{
			const int MAX_READ_COUNT = 20;

			while (!m_abortThread)
			{
				if (m_port.IsOpen)
				{
					lock (m_lockOperatePort)
					{
						int nReadCount = 0;
						while (m_port.BytesToRead > 0 && nReadCount++ < MAX_READ_COUNT)
						{
							string recvData = m_port.ReadLine();

							// 受信バッファへ格納
							if (m_lstBufferRecv.Count >= m_maxRecvBuffer)
								m_lstBufferRecv.RemoveAt(0);        // 古い物から消していく
							m_lstBufferRecv.Add(recvData);

							// コマンド結果受信用バッファへ格納
							if (recvData.StartsWith("$PMTK"))
							{
								m_lstBufferCmdResult.Add(recvData);
							}
						}
					}
				}
				Thread.Sleep(2);
			}
		}

		/// <summary>
		/// コマンド実行
		/// </summary>
		/// <param name="cmd">実行コマンド（チェックサムまで設定されていること）</param>
		/// <returns>コマンド実行に成功した場合はコマンド結果受信尿バッファ（m_lstBufferCmdResult）の結果格納位置を返す。 失敗した場合は-1を返す。</returns>
		public int sendCommand(string cmd)
		{
			int nCntRecv = -1;

			try
			{
				lock (m_lockOperatePort)
				{
					nCntRecv = m_lstBufferCmdResult.Count;  // コマンド実行結果の現在数

					// コマンド実行
					m_port.WriteLine(cmd);

					// コマンド文字列をバッファへ登録
					int nCntSend = m_lstBufferCmdSend.Count;
					m_lstBufferCmdSend.Add(cmd);
				}
			}
			catch (Exception)
			{
				// NOP
			}

			return nCntRecv;
		}

		/// <summary>
		/// コマンドの結果が返ってくるまで待つ。
		/// </summary>
		/// <param name="prefix">待機対象のコマンド文字列プリフィックス。 例）"$PMTK001"</param>
		/// <param name="idxBuf">参照するコマンド結果受信用バッファのインデックス。sendCommandの戻り値を渡す。 
		/// 未指定時はその時点のバッファ末尾からチェックする。</param>
		/// <param name="timeout">タイムアウトをmsecで指定する。 未指定時は5秒。</param>
		/// <param name="bExecDoEvents">待ち時間にApplication.DoEventsを呼び出すかどうか</param>
		/// <returns></returns>
		public string waitCommandResult(string prefix, int idxBuf = -1, int timeout = 5000, bool bExecDoEvents = false)
		{
			if (idxBuf < 0)
				idxBuf = (m_lstBufferCmdResult.Count > 0) ? m_lstBufferCmdResult.Count - 1 : 0;     // バッファ末尾からチェック
			else if (idxBuf > m_lstBufferCmdResult.Count)
				idxBuf = m_lstBufferCmdResult.Count;

			int elapsed = 0;
			while (elapsed < timeout)
			{
				lock (m_lockOperatePort)
				{
					for (int i = idxBuf; i < m_lstBufferCmdResult.Count; i++)
					{
						if (m_lstBufferCmdResult[i].StartsWith(prefix))
						{
							return m_lstBufferCmdResult[i];
						}
					}
				}

				if (bExecDoEvents)
				{
					Application.DoEvents();
				}
				elapsed += 10;
				Thread.Sleep(10);
			}
			return null;
		}

		/// <summary>
		/// 受信用バッファの内容を取得し、クリアする
		/// </summary>
		/// <returns>受信用バッファの内容</returns>
		public List<string> popRecvBuffer()
		{
			List<string> lstResult = null;

			lock (m_lockOperatePort)
			{
				lstResult = new List<string>(m_lstBufferRecv);
				m_lstBufferRecv.Clear();
			}
			return lstResult;
		}

		/// <summary>
		/// 送信用バッファの内容を取得する。
		/// </summary>
		/// <param name="Unacquired">trueならば未取得の物のみを返す。 falseなら全てを返す。</param>
		/// <returns>送信用バッファの内容</returns>
		public List<string> getSendBuffer(bool bUnacquired=true)
		{
			List<string> lstResult = null;

			lock (m_lockOperatePort)
			{
				int nCnt = m_lstBufferCmdSend.Count;
				if (nCnt > 0)
				{
					int nStartIndex = (bUnacquired) ? m_unacquiredIndexBufferCmdSend : 0;
					lstResult = new List<string>();

					for (int i = nStartIndex; i < nCnt; i++)
						lstResult.Add(m_lstBufferCmdSend[i]);

					if (bUnacquired)
						m_unacquiredIndexBufferCmdSend = nCnt;
				}
			}
			return lstResult;
		}
	}


	/// <summary>
	/// MTKコマンド等のユーティリティ
	/// </summary>
	public class MtkUtil
	{
		/// <summary>
		/// MTKコマンドプリフィックス
		/// </summary>
		public const string MTK_COMMAND_PREFIX = "$PMTK";

		/// <summary>
		/// ログ ブロックサイズ
		/// （2KBだと正しく取得できるもよう）
		/// </summary>
		public const int LOG_BLOCK_SIZE = 0x800;

		/// <summary>
		/// ログ セクタサイズ（64KB）
		/// </summary>
		public const int LOG_SECTOR_SIZE = 0x10000;

		/// <summary>
		/// ログヘッダーサイズ（byte）
		/// </summary>
		public const int LOG_HEADER_SIZE_BYTE = 512;

		/// <summary>
		/// ログ： Dynamic Setting Pattern（16進文字列）の長さ
		/// </summary>
		public const int LOG_DYNAMIC_SETTING_PATTERN_LENGTH = 32;
		/// <summary>
		/// ログ： Dynamic Setting Pattern 先頭のA文字列
		/// </summary>
		public const string LOG_DYNAMIC_SETTING_PATTERN_PREFIX_A = "AAAAAAAAAAAAAA";
		/// <summary>
		/// ログ： Dynamic Setting Pattern 末尾のB文字列
		/// </summary>
		public const string LOG_DYNAMIC_SETTING_PATTERN_SUFFIX_B = "BBBBBBBB";

		/// <summary>
		/// MTKチェックサム計算
		/// </summary>
		/// <param name="strMtkCommand">チェックサムを計算するMTKコマンド</param>
		/// <returns>チェックサム(2文字)</returns>
		public static string calcMtkChecksum(string strMtkCommand)
		{
			int nStart = (strMtkCommand.StartsWith("$") ? 1 : 0);
			int nEnd = strMtkCommand.Length - 1;
			if (strMtkCommand.Contains("*"))
				nEnd = strMtkCommand.IndexOf('*') - 1;

			if (nStart > nEnd)
				return "00";

			char[] aryChars = strMtkCommand.ToCharArray(nStart, nEnd - nStart + 1);
			byte checkSum = (byte)aryChars[0];
			for (int i = 1; i < aryChars.Length; i++)
			{
				checkSum ^= (byte)aryChars[i];
			}

			return string.Format("{0:x2}", checkSum);
		}

		/// <summary>
		/// コマンド文字列生成（引数なし用）
		/// </summary>
		/// <param name="cmdNo">コマンド番号</param>
		/// <returns>生成したコマンド文字列</returns>
		public static string makeCommandString(string cmdNo)
		{
			string strCmd = MTK_COMMAND_PREFIX + cmdNo + "*";
			strCmd += calcMtkChecksum(strCmd);
			return strCmd;
		}

		/// <summary>
		/// テストコマンド実行
		/// </summary>
		public static bool execCmdTest(GpsConnector gpsCon)
		{
			if (gpsCon == null)
				return false;

			string cmdTest = makeCommandString("000");
			string ackTestPrefix = MTK_COMMAND_PREFIX + "001,0";

			// テストコマンド送信
			int idx = gpsCon.sendCommand(cmdTest);

			if (idx >= 0)
			{
				string recvCmd = gpsCon.waitCommandResult(ackTestPrefix, idx, 1000);
				if (recvCmd != null)
				{
					if (recvCmd.Substring(ackTestPrefix.Length + 1, 1).Equals("3"))
						return true;
				}
			}
			return false;
		}

		/// <summary>
		/// 更新タイミング・レート取得
		/// </summary>
		/// <param name="gpsCon">Gpsコネクタのインスタンス</param>
		/// <returns>取得した更新レート情報。 取得に失敗した場合はnullを返す。</returns>
		public static UpdateRateInfo getUpdateRateInfo(GpsConnector gpsCon)
		{
			if (gpsCon == null)
				return null;

			UpdateRateInfo updInfo = new UpdateRateInfo();
			string recvCmd;
			int idxBuf;

			// ■更新タイミング取得
			string cmdGetTiming = makeCommandString("414");	  // PMTK_API_Q_NMEA_OUTPUT
			string cmdResTiming = MTK_COMMAND_PREFIX + "514";

			idxBuf = gpsCon.sendCommand(cmdGetTiming);
			if (idxBuf < 0)
				return null;

			recvCmd = gpsCon.waitCommandResult(cmdResTiming, idxBuf);
			if (recvCmd == null)
				return null;

			// コマンド結果から更新タイミングをセット
			if (!updInfo.setSentenceResult(recvCmd))
				return null;

			// ■更新レート取得
			string cmdGetRate = makeCommandString("400");        // PMTK_API_Q_FIX_CTL
			string cmdResRate = MTK_COMMAND_PREFIX + "500";

			idxBuf = gpsCon.sendCommand(cmdGetRate);
			if (idxBuf < 0)
				return null;

			recvCmd = gpsCon.waitCommandResult(cmdResRate, idxBuf);
			if (recvCmd == null)
				return updInfo;

			updInfo.setRateResult(recvCmd);
			return updInfo;
		}

		/// <summary>
		/// 更新タイミング設定
		/// </summary>
		/// <param name="gpsCon">Gpsコネクタのインスタンス</param>
		/// <param name="rateInfo">設定する更新タイミング情報</param>
		/// <returns>設定に成功した場合はtrueを返す。</returns>
		public static bool setUpdateTiming(GpsConnector gpsCon, UpdateRateInfo rateInfo)
		{
			if (gpsCon == null || rateInfo == null)
				return false;

			// 更新タイミングを設定
			string cmdSet = rateInfo.getCmdUpdateTiming();
			string cmdResPrifix = MTK_COMMAND_PREFIX + "001,314";

			int idxBuf = gpsCon.sendCommand(cmdSet);
			if (idxBuf < 0)
				return false;

			// 実行結果確認
			string recvCmd = gpsCon.waitCommandResult(cmdResPrifix, idxBuf);
			if (recvCmd == null || !recvCmd.StartsWith(cmdResPrifix))
				return false;

			if (!recvCmd.Substring(cmdResPrifix.Length + 1, 1).Equals("3"))
				return false;

			return true;
		}

		/// <summary>
		/// 更新レート設定
		/// </summary>
		/// <param name="gpsCon">Gpsコネクタのインスタンス</param>
		/// <param name="rate">更新レート</param>
		/// <returns>設定に成功した場合はtrueを返す。</returns>
		public static bool setUpdateRate(GpsConnector gpsCon, int rate)
		{
			if (gpsCon == null)
				return false;

			// 更新レートを設定
			string cmdSet = MTK_COMMAND_PREFIX + "300," + rate + ",0,0,0,0*";
			cmdSet += calcMtkChecksum(cmdSet);
			string cmdResPrifix = MTK_COMMAND_PREFIX + "001,300";

			int idxBuf = gpsCon.sendCommand(cmdSet);
			if (idxBuf < 0)
				return false;

			// 実行結果確認
			string recvCmd = gpsCon.waitCommandResult(cmdResPrifix, idxBuf);
			if (recvCmd == null || !recvCmd.StartsWith(cmdResPrifix))
				return false;

			if (!recvCmd.Substring(cmdResPrifix.Length + 1, 1).Equals("3"))
				return false;

			return true;
		}

		/// <summary>
		/// 更新タイミング・レート設定
		/// </summary>
		/// <param name="gpsCon">Gpsコネクタのインスタンス</param>
		/// <param name="rateInfo">設定する更新レート情報</param>
		/// <returns>設定に成功した場合はtrueを返す。</returns>
		public static bool setUpdateRateInfo(GpsConnector gpsCon, UpdateRateInfo rateInfo)
		{
			if (gpsCon != null && rateInfo != null)
				if (setUpdateTiming(gpsCon, rateInfo))			// 更新タイミング設定
					if (setUpdateRate(gpsCon, rateInfo.Rate))	// 更新レート設定
						return true;
			return false;
		}

		/// <summary>
		/// フォーマットレジスタ取得
		/// </summary>
		/// <param name="gpsCon">Gpsコネクタのインスタンス</param>
		/// <returns>取得結果を設定したフォーマットレジスタクラスのインスタンス。 取得失敗時はnullを返す。</returns>
		public static FormatRegisterInfo getFormatRegister(GpsConnector gpsCon)
		{
			FormatRegisterInfo regInfo = new FormatRegisterInfo();
			string recvCmd;
			int idxBuf;

			// フォーマットレジスタ取得
			string cmdGetFmtReg = makeCommandString("182,2,2");
			string cmdResFmtReg = MTK_COMMAND_PREFIX + "182,3,2";

			idxBuf = gpsCon.sendCommand(cmdGetFmtReg);
			if (idxBuf < 0)
				return null;

			recvCmd = gpsCon.waitCommandResult(cmdResFmtReg, idxBuf);
			if (idxBuf < 0)
				return null;

// TODO: $PMTK001,182,2, を取得し、コマンドの成否を判定する。（失敗時のことを考えると、先に見る必要がある）

			// コマンド結果からフォーマットレジスタ値をセット
			if (!regInfo.setRegisterResult(recvCmd))
				return null;

			return regInfo;
		}

		/// <summary>
		/// ログレコード数取得
		/// </summary>
		/// <param name="gpsCon">Gpsコネクタのインスタンス</param>
		/// <returns>取得したレコード数。 取得失敗時は-1を返す。</returns>
		public static int getLogRecordCount(GpsConnector gpsCon)
		{
			if (gpsCon == null)
				return -1;

			string recvCmd;
			int idxBuf;

			// ■ログレコード数取得
			string cmdGetLogRecordCount = makeCommandString("182,2,10");   // PMTK QUERY LOG STATUS
			string cmdResLogRecordCount = MTK_COMMAND_PREFIX + "182,3,10";

			idxBuf = gpsCon.sendCommand(cmdGetLogRecordCount);
			if (idxBuf < 0)
				return -1;

			recvCmd = gpsCon.waitCommandResult(cmdResLogRecordCount, idxBuf);
			if (recvCmd == null)
				return -1;

// TODO: $PMTK001,182,2, を取得し、コマンドの成否を判定する。（失敗時のことを考えると、先に見る必要がある）

			// コマンド結果からレコード数を取得  "$PMTK182,3,10,CNT*CHKSUM"
			char[] chDelimiter = { ',', '*' };
			string[] splitData = recvCmd.Split(chDelimiter);
			if (splitData.Length > 3)
			{
				int nCntData = 0;
				if (int.TryParse(splitData[3], NumberStyles.HexNumber, null, out nCntData))
				{
					return nCntData;
				}
			}

			return -1;
		}

		/// <summary>
		/// ログレコードアドレス取得
		/// </summary>
		/// <param name="gpsCon">Gpsコネクタのインスタンス</param>
		/// <returns>取得したログレコードアドレス。 取得失敗時は-1を返す。</returns>
		public static long getLogRecordAddress(GpsConnector gpsCon)
		{
			if (gpsCon == null)
				return -1;

			string recvCmd;
			int idxBuf;

			// ■ログレコードアドレス取得
			string cmdGetLogRecordAddr = makeCommandString("182,2,8");   // PMTK QUERY LOG STATUS
			string cmdResLogRecordAddr = MTK_COMMAND_PREFIX + "182,3,8";

			idxBuf = gpsCon.sendCommand(cmdGetLogRecordAddr);
			if (idxBuf < 0)
				return -1;

			recvCmd = gpsCon.waitCommandResult(cmdResLogRecordAddr, idxBuf);
			if (recvCmd == null)
				return -1;

// TODO: $PMTK001,182,2, を取得し、コマンドの成否を判定する。（失敗時のことを考えると、先に見る必要がある）

			// コマンド結果からレコード数を取得  "$PMTK182,3,8,RCD_ADDR*CHKSUM"
			char[] chDelimiter = { ',', '*' };
			string[] splitData = recvCmd.Split(chDelimiter);
			if (splitData.Length > 3)
			{
				long nRcdAddr = 0;
				if (long.TryParse(splitData[3], NumberStyles.HexNumber, null, out nRcdAddr))
				{
					return nRcdAddr;
				}
			}

			return -1;
		}

		/// <summary>
		/// ログ取得
		/// 中断時は、それまでに取得したログデータを返す。
		/// </summary>
		/// <param name="frmOwner">進捗ダイアログ等のオーナーフォーム</param>
		/// <param name="gpsCon">Gpsコネクタのインスタンス</param>
		/// <param name="logInfo">取得したログ情報</param>
		/// <returns>0: ログなし、1以上: ログ取得数、負値: 取得失敗</returns>
		public static int readLog(Form frmOwner, GpsConnector gpsCon, ref LogInfo logInfo)
		{
			int nLogCnt = 0;

			if (logInfo == null)
				logInfo = new LogInfo();
			logInfo.init();

			// ログレコード数取得
			logInfo.LogRecordNum = getLogRecordCount(gpsCon);
			if (logInfo.LogRecordNum <= 0)
				return 0;       // ログなし

			// ログアドレス取得（最終読込アドレスの確認）
			logInfo.LogAddress = getLogRecordAddress(gpsCon);
			if (logInfo.LogAddress < 0)
				return 0;       // ログなしを返す

			// プログレスダイアログ（モードレス）表示
			Form_Progress prgDlg = new Form_Progress();
			prgDlg.setProgressInfo(logInfo.LogRecordNum);
			prgDlg.Show(frmOwner);

			// 2KBずつログを取得
			for (long nAddr = 0; nAddr < logInfo.LogAddress; nAddr += LOG_BLOCK_SIZE)
			{
				// まずはイベントを処理
				Application.DoEvents();

				// 中断確認
				if (prgDlg.Cancel)
				{
					prgDlg.setStateMessage("中断します...");
					break;
				}

				// プログレスバー更新
				prgDlg.setStateMessage(nLogCnt + "/" + logInfo.LogRecordNum + "件目を取得中...");

				// ログ取得
				string recvCmd = null;
				int idxBuf;
				string cmdGetLog = makeCommandString("182,7," + nAddr.ToString("X") + "," + LOG_BLOCK_SIZE.ToString("X"));
				string cmdResult = MTK_COMMAND_PREFIX + "001,182,7,";
				string cmdResLog = MTK_COMMAND_PREFIX + "182,8";

				idxBuf = gpsCon.sendCommand(cmdGetLog);
				if (idxBuf >= 0)
				{
					recvCmd = gpsCon.waitCommandResult(cmdResult, idxBuf, 3000, true);
					if (recvCmd != null && recvCmd.Substring(cmdResult.Length, 1) == "3")
					{
						// 処理成功が返ってきた場合、返却されたログを取得
						recvCmd = gpsCon.waitCommandResult(cmdResLog, idxBuf, 3000, true);
					}
					else
					{
						recvCmd = null;
					}
				}
				if (recvCmd == null)
				{
					logInfo = null; // それまでに取得したログは削除
					nLogCnt = -1;   // エラーをセット
					break;
				}



			}

			// 進捗ダイアログ閉じ
			prgDlg.Dispose();

			return nLogCnt;
		}

		/// <summary>
		/// ログ取得（デバッグ用にファイルから読み込む）
		/// 中断時は、それまでに取得したログデータを返す。
		/// </summary>
		/// <param name="frmOwner">進捗ダイアログ等のオーナーフォーム</param>
		/// <param name="logInfo">取得したログ情報</param>
		/// <returns>0: ログなし、1以上: ログ取得数、負値: 取得失敗</returns>
		public static int readLogFromFile(Form frmOwner, ref LogInfo logInfo)
		{
			int nLogCnt = 0;

			if (logInfo == null)
				logInfo = new LogInfo();
			logInfo.init();
			logInfo.LogRecordNum = 20063;
			logInfo.LogAddress = 0x009E1F0;

			// ファイルオープン
			StreamReader sReader = null;
			try
			{
				sReader = new StreamReader("test2.log", System.Text.Encoding.Default);
			}
			catch (Exception)
			{
				return -1;
			}

			// プログレスダイアログ（モードレス）表示
			Form_Progress prgDlg = new Form_Progress();
			prgDlg.setProgressInfo(logInfo.LogRecordNum);
			prgDlg.Show(frmOwner);

			// 1セクタ(64KB)ずつログを取得し、解析処理へ流す
			int nCntSectors = (int)((logInfo.LogAddress + LOG_BLOCK_SIZE) / LOG_SECTOR_SIZE);

			for (int nSec = 0; nSec < nCntSectors; nSec++)
			{
				// まずはイベントを処理
				Application.DoEvents();
				// 中断確認
				if (prgDlg.Cancel)
				{
					prgDlg.setStateMessage("中断します...");
					break;
				}
				// プログレスバー更新
				prgDlg.setStateMessage(nLogCnt + "/" + logInfo.LogRecordNum + "件目を取得中...");

				// 1セクタを2KBずつ読み込む
				string sectorLog = "";
				for (int i = 0; i < (LOG_SECTOR_SIZE / LOG_BLOCK_SIZE); i++)
				{
					// ファイルには1行に2KBずつ保存されている
					if (sReader.Peek() >= 0)
					{
						string cmdResLog = sReader.ReadLine();

						// 先頭のコマンド部分、末尾のチェックサムを読み捨てるつつ、セクタ文字列へ結合
						int nCmdHeadLen = 0;
						if (cmdResLog.StartsWith("$PMTK182,8"))
						{
							nCmdHeadLen = "$PMTK182,8,00000000,".Length;
						}
						sectorLog += cmdResLog.Substring(nCmdHeadLen, cmdResLog.Length - nCmdHeadLen - 3);	// 3 = チェックサム分
					}
				}

				// ログを解析
				int nLogs = parseLogSector(sectorLog, ref logInfo);
				nLogCnt += nLogs;
			}

			// 進捗ダイアログ閉じ
			prgDlg.Dispose();

			if (!prgDlg.Cancel)
			{   // 読込成功
				logInfo.LogRecordNum = nLogCnt;
			}
			else
			{	// キャンセル
				logInfo.init();
				nLogCnt = 0;
			}

			return nLogCnt;
		}

		/// <summary>
		/// ログを1セクタ単位にバラす
		/// </summary>
		/// <param name="sectorLog">1セクタ分のログ文字列</param>
		/// <param name="logInfo">取得したログの格納先</param>
		/// <returns>読み込んだログ数</returns>
		public static int parseLogSector(string sectorLog, ref LogInfo logInfo)
		{
			// ログはセクタ（64KB）単位に読む。
			// セクタには必ずヘッダ（512Byte）があり、セクタに含まれるレコード数も分かる。
			// セクタのレコード領域が終わると、末尾は 0xFF で埋められている。
			// 2KBずつ取得するので、その末尾のチェックサムは取り除く。

			int nLogIndex = 0;  // ログ読込位置
			string strTmp;
			int nReadCnt = 0;
			int nSrcCnt = 0;
			FormatRegisterInfo formatRegister = new FormatRegisterInfo();

			try
			{
				// ヘッダの読込： 512Byte = 1024文字2;
				string header = sectorLog.Substring(nLogIndex, LOG_HEADER_SIZE_BYTE * 2);
				nLogIndex += LOG_HEADER_SIZE_BYTE * 2;

				// レコード数
				strTmp = header.Substring(2, 2) + header.Substring(0, 2);
				nSrcCnt = int.Parse(strTmp, NumberStyles.HexNumber);

				// フォーマットレジスタ
				strTmp = header.Substring(10, 2) + header.Substring(8, 2) + header.Substring(6, 2) + header.Substring(4, 2);
				formatRegister.setRegisterResult(strTmp);

				// Dynamic Setting Pattern（読み飛ばす）
				bool fDSP = true;
				while(fDSP)
				{
					strTmp = sectorLog.Substring(nLogIndex, LOG_DYNAMIC_SETTING_PATTERN_LENGTH);
					if (strTmp.StartsWith(LOG_DYNAMIC_SETTING_PATTERN_PREFIX_A) && strTmp.EndsWith(LOG_DYNAMIC_SETTING_PATTERN_SUFFIX_B))
						nLogIndex += LOG_DYNAMIC_SETTING_PATTERN_LENGTH;
					else
						fDSP = false;
				}

				// レコード読込
				int nRecordSize = formatRegister.getNeedLogSize(true) * 2;
				for (int i = 0; i < nSrcCnt; i++)
				{
					if (nLogIndex + nRecordSize > sectorLog.Length)
						break;

					strTmp = sectorLog.Substring(nLogIndex, nRecordSize);
					nLogIndex += nRecordSize;

					// ログ１レコードにパース
					LogItem logItem = new LogItem();
					if (logItem.setLog(formatRegister, strTmp))
					{
						logInfo.LogItemList.Add(logItem);
						nReadCnt++;
					}
				}
			}
			catch (Exception)
			{
				return 0;
			}


			return nReadCnt;
		}
	}


	/// <summary>
	/// 更新タイミング、更新レート
	/// </summary>
	public class UpdateRateInfo
	{
		public const int NumOfSentence = 18;

		public enum Sentence
		{
			TYPE_GLL = 0,        // Geographic Position - Latitude longitude
			TYPE_RMC,            // Recommended Minimum Specific GNSS Sentence
			TYPE_VTG,            // Course over Ground and Ground Speed
			TYPE_GGA,            // GPS Fix Data
			TYPE_GSA,            // GNSS DOPS and Active Satellites
			TYPE_GSV,            // GNSS Satellites in View
			TYPE_GRS,            // GRS range residuals
			TYPE_GST,            // Position error statistics
			TYPE_MALM = 13,
			TYPE_MEPH,
			TYPE_MDGP,
			TYPE_MDBG,
			TYPE_ZDA,
		}

		/// <summary>
		/// 更新レートに設定する値一覧
		/// </summary>
		public static ReadOnlyCollection<int> RateList = Array.AsReadOnly(new int[] { 200, 500, 1000, 2000, 3000, 4000, 5000 });

		/// <summary>
		/// 各NMEAセンテンスの更新タイミング
		/// </summary>
		protected int[] timing;

		public int getTiming(Sentence eType)
		{
			int nType = (int)eType;
			if (nType > 0 && nType < timing.Length)
				return timing[nType];
			else
				return -1;
		}

		public int GLL
		{
			get { return timing[(int)Sentence.TYPE_GLL]; }
			set { timing[(int)Sentence.TYPE_GLL] = value; }
		}
		public int RMC
		{
			get { return timing[(int)Sentence.TYPE_RMC]; }
			set { timing[(int)Sentence.TYPE_RMC] = value; }
		}
		public int VTG
		{
			get { return timing[(int)Sentence.TYPE_VTG]; }
			set { timing[(int)Sentence.TYPE_VTG] = value; }
		}
		public int GGA
		{
			get { return timing[(int)Sentence.TYPE_GGA]; }
			set { timing[(int)Sentence.TYPE_GGA] = value; }
		}
		public int GSA
		{
			get { return timing[(int)Sentence.TYPE_GSA]; }
			set { timing[(int)Sentence.TYPE_GSA] = value; }
		}
		public int GSV
		{
			get { return timing[(int)Sentence.TYPE_GSV]; }
			set { timing[(int)Sentence.TYPE_GSV] = value; }
		}
		public int GRS
		{
			get { return timing[(int)Sentence.TYPE_GRS]; }
			set { timing[(int)Sentence.TYPE_GRS] = value; }
		}
		public int GST
		{
			get { return timing[(int)Sentence.TYPE_GST]; }
			set { timing[(int)Sentence.TYPE_GST] = value; }
		}
		public int MALM
		{
			get { return timing[(int)Sentence.TYPE_MALM]; }
			set { timing[(int)Sentence.TYPE_MALM] = value; }
		}
		public int MEPH
		{
			get { return timing[(int)Sentence.TYPE_MEPH]; }
			set { timing[(int)Sentence.TYPE_MEPH] = value; }
		}
		public int MDGP
		{
			get { return timing[(int)Sentence.TYPE_MDGP]; }
			set { timing[(int)Sentence.TYPE_MDGP] = value; }
		}
		public int MDBG
		{
			get { return timing[(int)Sentence.TYPE_MDBG]; }
			set { timing[(int)Sentence.TYPE_MDBG] = value; }
		}
		public int ZDA
		{
			get { return timing[(int)Sentence.TYPE_ZDA]; }
			set { timing[(int)Sentence.TYPE_ZDA] = value; }
		}

		/// <summary>
		/// 更新レート
		/// </summary>
		protected int rate;

		/// <summary>
		/// 更新レート
		/// </summary>
		public int Rate
		{
			get { return rate; }
			set {
				int newRate = 0;
				for (int i = 0; i < RateList.Count; i++)
				{
					newRate = RateList[i];
					if (value <= newRate)
						break;
				}
				rate = newRate;
			}
		}

		/// <summary>
		/// コンストラクタ
		/// </summary>
		public UpdateRateInfo()
		{
			timing = new int[NumOfSentence];
			for (int i = 0; i < NumOfSentence; i++)
				timing[i] = 0;

			rate = 1000;
		}

		/// <summary>
		/// 更新タイミング更新用コマンド文字列取得
		/// </summary>
		public string getCmdUpdateTiming()
		{
			string cmdText = MtkUtil.MTK_COMMAND_PREFIX + "314";
			for (int i = 0; i < NumOfSentence; i++)
				cmdText += "," + timing[i];
			cmdText += "*";
			cmdText += MtkUtil.calcMtkChecksum(cmdText);
			return cmdText;
		}

		/// <summary>
		/// コマンド結果から、更新タイミングセットする。
		/// </summary>
		/// <param name="cmdResult">更新タイミング取得コマンド結果（$PMTK514： $PMTK414の戻り値）</param>
		/// <returns>セットに成功した時はtrueを返す</returns>
		public bool setSentenceResult(string cmdResult)
		{
			// 実行結果からデータ部分を切り出す「$PMTK514,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0*2F」
			string strRate;
			if (cmdResult.StartsWith(MtkUtil.MTK_COMMAND_PREFIX))
				strRate = cmdResult.Substring(9, cmdResult.Length - 12);
			else
				strRate = cmdResult;

			// 先頭から順にint化
			string[] aUpdRate = strRate.Split(',');
			int[] aNewTiming = new int[NumOfSentence];
			int nCnt = Math.Min(aUpdRate.Length, NumOfSentence);
			for (int i = 0; i < nCnt; i++)
			{
				if (!int.TryParse(aUpdRate[i], out aNewTiming[i]))
				{
					return false;	// 変換不能
				}
			}

			// メンバ変数へ反映
			aNewTiming.CopyTo(timing, 0);
			return true;
		}

		/// <summary>
		/// コマンド結果から、更新レートをセットする。
		/// </summary>
		/// <param name="cmdResult">更新レート取得コマンド結果（$PMTK500： $PMTK400の戻り値）</param>
		/// <returns>セットに成功した時はtrueを返す</returns>
		public bool setRateResult(string cmdResult)
		{
			// 実行結果からデータ部分を切り出す「$PMTK500,1000,0,0,0,0*1A」
			string strRate;
			if (cmdResult.StartsWith(MtkUtil.MTK_COMMAND_PREFIX))
				strRate = cmdResult.Substring(9, cmdResult.Length - 12);
			else
				strRate = cmdResult;

			string[] aUpdRate = strRate.Split(',');
			int rateResult = 0;
			if (int.TryParse(aUpdRate[0], out rateResult))
			{
				Rate = rateResult;
				return true;
			}
			return true;
		}
	}


	/// <summary>
	/// フォーマットレジスタ
	/// </summary>
	public class FormatRegisterInfo
	{
		public const int REGISTER_VALUE_ARRAY_SIZE = 4;

		/// <summary>
		/// レジスタ値（リトルエンディアン）
		/// </summary>
		protected byte[] m_registerVal;

		/// <summary>
		/// レジスタ項目名一覧
		/// </summary>
		public static ReadOnlyCollection<string> ItemNameList = Array.AsReadOnly(new string[] {
				"UTC", "VALID", "LATITUDE", "LONGITUDE", "HEIGHT", "SPEED", "TRACK", "DSTA",
				"DAGE", "PDOP", "HDOP", "VDOP", "NSAT", "SID", "ELE", "AZI",
				"SNR", "RCR", "MS"
			});

		public enum Register
		{
			REG_UTC = 0,
			REG_VALID,
			REG_LATITUDE,
			REG_LONGITUDE,
			REG_HEIGHT,
			REG_SPEED,
			REG_TRACK,
			REG_DSTA,
			REG_DAGE,
			REG_PDOP,
			REG_HDOP,
			REG_VDOP,
			REG_NSAT,
			REG_SID,
			REG_ELE,
			REG_AZI,
			REG_SNR,
			REG_RCR,
			REG_MS,
		}

		public static ReadOnlyCollection<int> RegisterValueLength = Array.AsReadOnly(new int[] {
			4,	// UTC
			2,	// VALID
			8,	// LATITUDE
			8,	// LONGITUDE
			4,	// HEIGHT
			4,	// SPEED
			4,	// TRACK
			2,	// DSTA
			4,	// DAGE
			2,	// PDOP
			2,	// HDOP
			2,	// VDOP
			2,	// NSAT
			4,	// SID
			2,	// ELE
			2,	// AZI
			2,	// SNR
			2,	// RCR
			2,	// MS
		});

		/// <summary>
		/// コンストラクタ
		/// </summary>
		public FormatRegisterInfo()
		{
			m_registerVal = new byte[REGISTER_VALUE_ARRAY_SIZE];
		}

		/// <summary>
		/// 初期化
		/// </summary>
		public void init()
		{
			for (int i = 0; i < REGISTER_VALUE_ARRAY_SIZE; i++)
				m_registerVal[i] = 0;
		}

		/// <summary>
		/// レジスタ値取得
		/// </summary>
		/// <param name="index">レジスタのインデックス</param>
		/// <returns>レジスタ値(0/1=false/true)</returns>
		public bool getValue(int index)
		{
			switch (index)
			{
				case (int)Register.REG_UTC:			return UTC;
				case (int)Register.REG_VALID:		return VALID;
				case (int)Register.REG_LATITUDE:	return LATITUDE;
				case (int)Register.REG_LONGITUDE:	return LONGITUDE;
				case (int)Register.REG_HEIGHT:		return HEIGHT;
				case (int)Register.REG_SPEED:		return SPEED;
				case (int)Register.REG_TRACK:		return TRACK;
				case (int)Register.REG_DSTA:		return DSTA;
				case (int)Register.REG_DAGE:		return DAGE;
				case (int)Register.REG_PDOP:		return PDOP;
				case (int)Register.REG_HDOP:		return HDOP;
				case (int)Register.REG_VDOP:		return VDOP;
				case (int)Register.REG_NSAT:		return NSAT;
				case (int)Register.REG_SID:			return SID;
				case (int)Register.REG_ELE:			return ELE;
				case (int)Register.REG_AZI:			return AZI;
				case (int)Register.REG_SNR:			return SNR;
				case (int)Register.REG_RCR:			return RCR;
				case (int)Register.REG_MS:			return MS;
			}
			return false;
		}

		// レジスタ値プロパティ(0～7)
		public bool UTC
		{
			get { return (m_registerVal[0] & 0x01) != 0; }
			set { m_registerVal[0] = (byte)(m_registerVal[0] & ~0x01 | (value ? 0x01 : 0x00)); }
		}
		public bool VALID
		{
			get { return (m_registerVal[0] & 0x02) != 0; }
			set { m_registerVal[0] = (byte)(m_registerVal[0] & ~0x02 | (value ? 0x02 : 0x00)); }
		}
		public bool LATITUDE
		{
			get { return (m_registerVal[0] & 0x04) != 0; }
			set { m_registerVal[0] = (byte)(m_registerVal[0] & ~0x04 | (value ? 0x04 : 0x00)); }
		}
		public bool LONGITUDE
		{
			get { return (m_registerVal[0] & 0x08) != 0; }
			set { m_registerVal[0] = (byte)(m_registerVal[0] & ~0x08 | (value ? 0x08 : 0x00)); }
		}
		public bool HEIGHT
		{
			get { return (m_registerVal[0] & 0x10) != 0; }
			set { m_registerVal[0] = (byte)(m_registerVal[0] & ~0x10 | (value ? 0x10 : 0x00)); }
		}
		public bool SPEED
		{
			get { return (m_registerVal[0] & 0x20) != 0; }
			set { m_registerVal[0] = (byte)(m_registerVal[0] & ~0x20 | (value ? 0x20 : 0x00)); }
		}
		public bool TRACK
		{
			get { return (m_registerVal[0] & 0x40) != 0; }
			set { m_registerVal[0] = (byte)(m_registerVal[0] & ~0x40 | (value ? 0x40 : 0x00)); }
		}
		public bool DSTA
		{
			get { return (m_registerVal[0] & 0x80) != 0; }
			set { m_registerVal[0] = (byte)(m_registerVal[0] & ~0x80 | (value ? 0x80 : 0x00)); }
		}
		// レジスタ値プロパティ(8～15)
		public bool DAGE
		{
			get { return (m_registerVal[1] & 0x01) != 0; }
			set { m_registerVal[1] = (byte)(m_registerVal[1] & ~0x01 | (value ? 0x01 : 0x00)); }
		}
		public bool PDOP
		{
			get { return (m_registerVal[1] & 0x02) != 0; }
			set { m_registerVal[1] = (byte)(m_registerVal[1] & ~0x02 | (value ? 0x02 : 0x00)); }
		}
		public bool HDOP
		{
			get { return (m_registerVal[1] & 0x04) != 0; }
			set { m_registerVal[1] = (byte)(m_registerVal[1] & ~0x04 | (value ? 0x04 : 0x00)); }
		}
		public bool VDOP
		{
			get { return (m_registerVal[1] & 0x08) != 0; }
			set { m_registerVal[1] = (byte)(m_registerVal[1] & ~0x08 | (value ? 0x08 : 0x00)); }
		}
		public bool NSAT
		{
			get { return (m_registerVal[1] & 0x10) != 0; }
			set { m_registerVal[1] = (byte)(m_registerVal[1] & ~0x10 | (value ? 0x10 : 0x00)); }
		}
		public bool SID
		{
			get { return (m_registerVal[1] & 0x20) != 0; }
			set { m_registerVal[1] = (byte)(m_registerVal[1] & ~0x20 | (value ? 0x20 : 0x00)); }
		}
		public bool ELE
		{
			get { return (m_registerVal[1] & 0x40) != 0; }
			set { m_registerVal[1] = (byte)(m_registerVal[1] & ~0x40 | (value ? 0x40 : 0x00)); }
		}
		public bool AZI
		{
			get { return (m_registerVal[1] & 0x80) != 0; }
			set { m_registerVal[1] = (byte)(m_registerVal[1] & ~0x80 | (value ? 0x80 : 0x00)); }
		}
		// レジスタ値プロパティ(16～18)
		public bool SNR
		{
			get { return (m_registerVal[2] & 0x01) != 0; }
			set { m_registerVal[2] = (byte)(m_registerVal[2] & ~0x01 | (value ? 0x01 : 0x00)); }
		}
		public bool RCR
		{
			get { return (m_registerVal[2] & 0x02) != 0; }
			set { m_registerVal[2] = (byte)(m_registerVal[2] & ~0x02 | (value ? 0x02 : 0x00)); }
		}
		public bool MS
		{
			get { return (m_registerVal[2] & 0x04) != 0; }
			set { m_registerVal[2] = (byte)(m_registerVal[2] & ~0x04 | (value ? 0x04 : 0x00)); }
		}

		/// <summary>
		/// コマンド結果から、フォーマットレジスタ値をセットする。
		/// </summary>
		/// <param name="cmdResult">フォーマットレジスタ取得コマンド結果（$PMTK182,3,2,oooooooo*FF： $PMTK182,2,2*39の戻り値）</param>
		/// <returns>セットに成功した時はtrueを返す</returns>
		public bool setRegisterResult(string cmdResult)
		{
			bool bRet = true;

			// 実行結果からデータ部分を切り出す「$PMTK182,3,2,0002003F*63」
			string strRegister;
			if (cmdResult.StartsWith(MtkUtil.MTK_COMMAND_PREFIX))
				strRegister = cmdResult.Substring(13, 8);
			else
				strRegister = cmdResult;

			if (strRegister.Length == 8)
			{
				for (int i = 0; i < REGISTER_VALUE_ARRAY_SIZE; i++)
				{
					int index = REGISTER_VALUE_ARRAY_SIZE - 1 - i;
					if (!byte.TryParse(strRegister.Substring(i * 2, 2), NumberStyles.AllowHexSpecifier, NumberFormatInfo.CurrentInfo, out m_registerVal[index]))
					{
						m_registerVal[index] = 0;
						bRet = false;
					}
				}
			}

			return bRet;
		}

		/// <summary>
		/// 現在のフォーマットレジスタ値に応じた必要ログサイズ（byte）を計算する。
		/// </summary>
		/// <param name="bIncludeCheckSum">チェックサム分も含むかどうか</param>
		/// <returns></returns>
		public int getNeedLogSize(bool bIncludeCheckSum)
		{
			int nLogSize = 0;

			// レジスタのON/OFFに合わせて加算していく
			for (int i = 0; i < ItemNameList.Count; i++)
			{
				if (getValue(i))
					nLogSize += RegisterValueLength[i];
			}

			// チェックサム分を加算
			if (bIncludeCheckSum)
				nLogSize += 2;

			return nLogSize;
		}

		/// <summary>
		/// 各アイテムのバイト数を返す。
		/// </summary>
		/// <param name="eReg">アイテム</param>
		/// <returns>バイト数</returns>
		public static int getRegisterValueLength(Register eReg)
		{
			return RegisterValueLength[(int)eReg];
		}
	}


	/// <summary>
	/// ログ１レコードのアイテム
	/// </summary>
	public class LogItem
	{
		/// <summary>
		/// フォーマットレジスタ（項目の有効／無効判断用）
		/// </summary>
		FormatRegisterInfo m_fmtRegInfo;

		/// <summary>
		/// UTC（time_t）
		/// </summary>
		protected uint m_nUtc;

		public uint UTC
		{
			get { return m_nUtc; }
			set { m_nUtc = value; }
		}
		private static DateTime UNIX_EPOCH = new DateTime(1970, 1, 1, 0, 0, 0);
		/// <summary>
		/// UTC(time_t)をDateTimeに変換した値
		/// </summary>
		public DateTime UTC_Date { get { return UNIX_EPOCH.AddSeconds(Convert.ToDouble(m_nUtc)); } }

		/// <summary>
		/// VALID (WORD)
		/// </summary>
		protected ushort m_nValid;

		public ushort VALID {
			get { return m_nValid; }
			set { m_nValid = value; }
		}
		public bool VALID_NOFIX { get { return (m_nValid & 0x0001) > 0; } }
		public bool VALID_SPS { get { return (m_nValid & 0x0002) > 0; } }
		public bool VALID_DGPS { get { return (m_nValid & 0x0004) > 0; } }
		public bool VALID_PPS { get { return (m_nValid & 0x0008) > 0; } }
		public bool VALID_RTK { get { return (m_nValid & 0x0010) > 0; } }
		public bool VALID_FRTK { get { return (m_nValid & 0x0020) > 0; } }
		public bool VALID_ESTIMATED { get { return (m_nValid & 0x0040) > 0; } }
		public bool VALID_MANUAL { get { return (m_nValid & 0x0080) > 0; } }
		public bool VALID_SIMULATOR { get { return (m_nValid & 0x0100) > 0; } }

		/// <summary>
		/// LATITUDE (double)
		/// </summary>
		protected double m_lfLatitude;

		public double LATITUDE
		{
			get { return m_lfLatitude; }
			set { m_lfLatitude = value; }
		}

		/// <summary>
		/// LONGITUDE (double)
		/// </summary>
		protected double m_lfLongitude;

		public double LONGITUDE
		{
			get { return m_lfLongitude; }
			set { m_lfLongitude = value; }
		}

		/// <summary>
		/// HEIGHT (float)
		/// </summary>
		protected float m_fHeight;

		public float HEIGHT
		{
			get { return m_fHeight; }
			set { m_fHeight = value; }
		}

		/// <summary>
		/// SPEED (float)
		/// </summary>
		protected float m_fSpeed;

		public float SPEED
		{
			get { return m_fSpeed; }
			set { m_fSpeed = value; }
		}

		/// <summary>
		/// TRACK (float)
		/// </summary>
		protected float m_fTrack;

		public float TRACK
		{
			get { return m_fTrack; }
			set { m_fTrack = value; }
		}

		/// <summary>
		/// DSTA (WORD)
		/// Differential GPS reference station ID.
		/// </summary>
		protected ushort m_nDsta;

		public ushort DSTA
		{
			get { return m_nDsta; }
			set { m_nDsta = value; }
		}

		/// <summary>
		/// DAGE (float)
		/// Differential GPS correction data age
		/// </summary>
		protected float m_fDage;

		public float DAGE
		{
			get { return m_fDage; }
			set { m_fDage = value; }
		}

		/// <summary>
		/// PDOP (WORD)
		/// </summary>
		protected ushort m_nPdop;

		public ushort PDOP
		{
			get { return m_nPdop; }
			set { m_nPdop = value; }
		}

		/// <summary>
		/// HDOP (WORD)
		/// </summary>
		protected ushort m_nHdop;

		public ushort HDOP
		{
			get { return m_nHdop; }
			set { m_nHdop = value; }
		}

		/// <summary>
		/// VDOP (WORD)
		/// </summary>
		protected ushort m_nVdop;

		public ushort VDOP
		{
			get { return m_nVdop; }
			set { m_nVdop = value; }
		}

		/// <summary>
		/// NSAT (WORD)
		/// </summary>
		protected ushort m_nNsat;

		public ushort NSAT
		{
			get { return m_nNsat; }
			set { m_nNsat = value; }
		}
		public int NSAT_IN_VIEW { get { return m_nNsat & 0x00ff; } }
		public int NSAT_IN_USE { get { return (m_nNsat & 0xff00) >> 8; } }

		/// <summary>
		/// SID (DWORD)
		/// </summary>
		protected uint m_nSid;

		public uint SID
		{
			get { return m_nSid; }
			set { m_nSid = value; }
		}
		public int SID_ID { get { return (int)(m_nSid & 0x7f); } }
		public bool SID_INUSE { get { return (m_nSid & 0x80) > 0; } }
		public int SID_INVIEW {  get { return (int)((m_nSid & 0xffff0000) >> 16); } }

		/// <summary>
		/// ELE (short)
		/// Elevation angle in degree of the SID
		/// </summary>
		protected short m_nEle;

		public short ELE
		{
			get { return m_nEle; }
			set { m_nEle = value; }
		}

		/// <summary>
		/// ALI (WORD)
		/// Azimuth angle in degree of the SID
		/// </summary>
		protected ushort m_nAli;

		public ushort ALI
		{
			get { return m_nAli; }
			set { m_nAli = value; }
		}

		/// <summary>
		/// SNR (WORD)
		/// </summary>
		protected ushort m_nSnr;

		public ushort SNR
		{
			get { return m_nSnr; }
			set { m_nSnr = value; }
		}

		/// <summary>
		/// RCR (WORD)
		/// Record reason
		/// </summary>
		protected ushort m_nRcr;

		public ushort RCR
		{
			get { return m_nRcr; }
			set { m_nRcr = value; }
		}
		public bool RCR_TIME { get { return (m_nRcr & 0x01) > 0; } }
		public bool RCR_SPEED { get { return (m_nRcr & 0x02) > 0; } }
		public bool RCR_DISTANCE { get { return (m_nRcr & 0x04) > 0; } }
		public bool RCR_BUTTON { get { return (m_nRcr & 0x08) > 0; } }

		/// <summary>
		/// MS (WORD)
		/// The milliseconds part of the current recording time.
		/// The second part of the current time should consult to the field of the UTC.
		/// </summary>
		protected ushort m_nMs;

		public ushort MS
		{
			get { return m_nMs; }
			set { m_nMs = value; }
		}

		/// <summary>
		/// コンストラクタ
		/// </summary>
		public LogItem()
		{
			m_fmtRegInfo = new FormatRegisterInfo();
			init();
		}

		/// <summary>
		/// 初期化
		/// </summary>
		public void init()
		{
			m_fmtRegInfo.init();
			m_nUtc = 0;
			m_nValid = 0;
			m_lfLatitude = 0.0;
			m_lfLongitude = 0.0;
			m_fHeight = 0.0f;
			m_fSpeed = 0.0f;
			m_fTrack = 0.0f;
			m_nDsta = 0;
			m_fDage = 0.0f;
			m_nPdop = 0;
			m_nHdop = 0;
			m_nVdop = 0;
			m_nNsat = 0;
			m_nSid = 0;
			m_nEle = 0;
			m_nAli = 0;
			m_nSnr = 0;
			m_nRcr = 0;
			m_nMs = 0;
		}

		/// <summary>
		/// ログをセットする。
		/// </summary>
		/// <param name="fmtRegInfo">読込対象がセットされたフォーマットレジスタ</param>
		/// <param name="strRaw">読込対象（ログ文字列）</param>
		public bool setLog(FormatRegisterInfo fmtRegInfo, string strRaw)
		{
			if (fmtRegInfo == null || strRaw == null)
				return false;

			// フォーマットレジスタをセットし、文字列長が必要サイズに達しているかチェック
			m_fmtRegInfo = fmtRegInfo;
			if (strRaw.Length < (m_fmtRegInfo.getNeedLogSize(false) * 2))
				return false;

			try
			{
				int nIdx = 0;
				int nLen = 0;

				if (m_fmtRegInfo.UTC)
				{
					nLen = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_UTC) * 2;
					string strTmp = "";
					for (int nCh = 0; nCh < nLen; nCh += 2)
						strTmp = strRaw.Substring(nIdx + nCh, 2) + strTmp;
					m_nUtc = uint.Parse(strTmp, NumberStyles.HexNumber);
					nIdx += nLen;
				}

				if (m_fmtRegInfo.VALID)
				{
					nLen = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_VALID) * 2;
					string strTmp = "";
					for (int nCh = 0; nCh < nLen; nCh += 2)
						strTmp = strRaw.Substring(nIdx + nCh, 2) + strTmp;
					m_nValid = ushort.Parse(strTmp, NumberStyles.HexNumber);
					nIdx += nLen;
				}

				if (m_fmtRegInfo.LATITUDE)
				{
					int nBytes = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_LATITUDE);
					nLen = nBytes * 2;
					byte[] aryByte = new byte[nBytes];
					for (int nCh = 0; nCh < nLen; nCh += 2)
					{
						aryByte[nCh / 2] = byte.Parse(strRaw.Substring(nIdx + nCh, 2), NumberStyles.HexNumber);
					}
					m_lfLatitude = BitConverter.ToDouble(aryByte, 0);
					nIdx += nLen;
				}

				if (m_fmtRegInfo.LONGITUDE)
				{
					int nBytes = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_LONGITUDE);
					nLen = nBytes * 2;
					byte[] aryByte = new byte[nBytes];
					for (int nCh = 0; nCh < nLen; nCh += 2)
					{
						aryByte[nCh / 2] = byte.Parse(strRaw.Substring(nIdx + nCh, 2), NumberStyles.HexNumber);
					}
					m_lfLongitude = BitConverter.ToDouble(aryByte, 0);
					nIdx += nLen;
				}

				if (m_fmtRegInfo.HEIGHT)
				{
					int nBytes = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_HEIGHT);
					nLen = nBytes * 2;
					byte[] aryByte = new byte[nBytes];
					for (int nCh = 0; nCh < nLen; nCh += 2)
					{
						aryByte[nCh / 2] = byte.Parse(strRaw.Substring(nIdx + nCh, 2), NumberStyles.HexNumber);
					}
					m_fHeight = BitConverter.ToSingle(aryByte, 0);
					nIdx += nLen;
				}

				if (m_fmtRegInfo.SPEED)
				{
					int nBytes = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_SPEED);
					nLen = nBytes * 2;
					byte[] aryByte = new byte[nBytes];
					for (int nCh = 0; nCh < nLen; nCh += 2)
					{
						aryByte[nCh / 2] = byte.Parse(strRaw.Substring(nIdx + nCh, 2), NumberStyles.HexNumber);
					}
					m_fSpeed = BitConverter.ToSingle(aryByte, 0);
					nIdx += nLen;
				}

				if (m_fmtRegInfo.TRACK)
				{
					int nBytes = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_TRACK);
					nLen = nBytes * 2;
					byte[] aryByte = new byte[nBytes];
					for (int nCh = 0; nCh < nLen; nCh += 2)
					{
						aryByte[nCh / 2] = byte.Parse(strRaw.Substring(nIdx + nCh, 2), NumberStyles.HexNumber);
					}
					m_fTrack = BitConverter.ToSingle(aryByte, 0);
					nIdx += nLen;					
				}

				if (m_fmtRegInfo.DSTA)
				{
					nLen = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_DSTA) * 2;
					string strTmp = "";
					for (int nCh = 0; nCh < nLen; nCh += 2)
						strTmp = strRaw.Substring(nIdx + nCh, 2) + strTmp;
					m_nDsta = ushort.Parse(strTmp, NumberStyles.HexNumber);
					nIdx += nLen;
				}

				if (m_fmtRegInfo.DAGE)
				{
					int nBytes = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_DAGE);
					nLen = nBytes * 2;
					byte[] aryByte = new byte[nBytes];
					for (int nCh = 0; nCh < nLen; nCh += 2)
					{
						aryByte[nCh / 2] = byte.Parse(strRaw.Substring(nIdx + nCh, 2), NumberStyles.HexNumber);
					}
					m_fDage = BitConverter.ToSingle(aryByte, 0);
					nIdx += nLen;
				}

				if (m_fmtRegInfo.PDOP)
				{
					nLen = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_PDOP) * 2;
					string strTmp = "";
					for (int nCh = 0; nCh < nLen; nCh += 2)
						strTmp = strRaw.Substring(nIdx + nCh, 2) + strTmp;
					m_nPdop = ushort.Parse(strTmp, NumberStyles.HexNumber);
					nIdx += nLen;
				}

				if (m_fmtRegInfo.HDOP)
				{
					nLen = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_HDOP) * 2;
					string strTmp = "";
					for (int nCh = 0; nCh < nLen; nCh += 2)
						strTmp = strRaw.Substring(nIdx + nCh, 2) + strTmp;
					m_nHdop = ushort.Parse(strTmp, NumberStyles.HexNumber);
					nIdx += nLen;
				}

				if (m_fmtRegInfo.VDOP)
				{
					nLen = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_VDOP) * 2;
					string strTmp = "";
					for (int nCh = 0; nCh < nLen; nCh += 2)
						strTmp = strRaw.Substring(nIdx + nCh, 2) + strTmp;
					m_nVdop = ushort.Parse(strTmp, NumberStyles.HexNumber);
					nIdx += nLen;
				}

				if (m_fmtRegInfo.NSAT)
				{
					nLen = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_NSAT) * 2;
					string strTmp = "";
					for (int nCh = 0; nCh < nLen; nCh += 2)
						strTmp = strRaw.Substring(nIdx + nCh, 2) + strTmp;
					m_nNsat = ushort.Parse(strTmp, NumberStyles.HexNumber);
					nIdx += nLen;
				}

				if (m_fmtRegInfo.SID)
				{
					nLen = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_SID) * 2;
					string strTmp = "";
					for (int nCh = 0; nCh < nLen; nCh += 2)
						strTmp = strRaw.Substring(nIdx + nCh, 2) + strTmp;
					m_nSid = uint.Parse(strTmp, NumberStyles.HexNumber);
					nIdx += nLen;
				}

				if (m_fmtRegInfo.ELE)
				{
					nLen = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_ELE) * 2;
					string strTmp = "";
					for (int nCh = 0; nCh < nLen; nCh += 2)
						strTmp = strRaw.Substring(nIdx + nCh, 2) + strTmp;
					m_nEle = short.Parse(strTmp, NumberStyles.HexNumber);
					nIdx += nLen;
				}

				if (m_fmtRegInfo.AZI)
				{
					nLen = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_AZI) * 2;
					string strTmp = "";
					for (int nCh = 0; nCh < nLen; nCh += 2)
						strTmp = strRaw.Substring(nIdx + nCh, 2) + strTmp;
					m_nAli = ushort.Parse(strTmp, NumberStyles.HexNumber);
					nIdx += nLen;
				}

				if (m_fmtRegInfo.SNR)
				{
					nLen = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_SNR) * 2;
					string strTmp = "";
					for (int nCh = 0; nCh < nLen; nCh += 2)
						strTmp = strRaw.Substring(nIdx + nCh, 2) + strTmp;
					m_nSnr = ushort.Parse(strTmp, NumberStyles.HexNumber);
					nIdx += nLen;
				}

				if (m_fmtRegInfo.RCR)
				{
					nLen = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_RCR) * 2;
					string strTmp = "";
					for (int nCh = 0; nCh < nLen; nCh += 2)
						strTmp = strRaw.Substring(nIdx + nCh, 2) + strTmp;
					m_nRcr = ushort.Parse(strTmp, NumberStyles.HexNumber);
					nIdx += nLen;
				}

				if (m_fmtRegInfo.MS)
				{
					nLen = FormatRegisterInfo.getRegisterValueLength(FormatRegisterInfo.Register.REG_MS) * 2;
					string strTmp = "";
					for (int nCh = 0; nCh < nLen; nCh += 2)
						strTmp = strRaw.Substring(nIdx + nCh, 2) + strTmp;
					m_nMs = ushort.Parse(strTmp, NumberStyles.HexNumber);
					nIdx += nLen;
				}
			}
			catch(Exception ex)
			{
				System.Diagnostics.Debug.WriteLine(ex);
				return false;
			}

			return true;
		}
	}


	/// <summary>
	/// ログ情報
	/// </summary>
	public class LogInfo
	{
		/// <summary>
		/// ログレコード数
		/// </summary>
		protected int m_nLogRedcord;

		public int LogRecordNum
		{
			get { return m_nLogRedcord; }
			set { m_nLogRedcord = value; }
		}

		/// <summary>
		/// ログアドレス
		/// </summary>
		protected long m_nLogAddr;

		public long LogAddress
		{
			get { return m_nLogAddr; }
			set { m_nLogAddr = value; }
		}

		/// <summary>
		/// ログ一覧
		/// </summary>
		protected List<LogItem> m_lstLogItem;

		public List<LogItem> LogItemList
		{
			get { return m_lstLogItem; }
		}

		/// <summary>
		/// コンストラクタ
		/// </summary>
		public LogInfo()
		{
			init();
		}

		/// <summary>
		/// ログ情報初期化
		/// </summary>
		public void init()
		{
			m_nLogRedcord = 0;
			m_nLogAddr = 0;
			m_lstLogItem = new List<LogItem>();
		}



		// ログ情報保存（シリアライズ）

	}
}
