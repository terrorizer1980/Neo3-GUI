/* eslint-disable */
import React from "react";
import "antd/dist/antd.css";
import { observer, inject } from "mobx-react";
import {
  Input,
  PageHeader,
  Modal,
  Select,
  Row,
  Col,
  Form,
  message,
  Button,
} from "antd";
import Datatrans from "../Common/datatrans";
import { Layout } from "antd";
import "../../static/css/wallet.css";
import Sync from "../sync";
import { SwapOutlined } from "@ant-design/icons";
import { withRouter } from "react-router-dom";
import { withTranslation } from "react-i18next";
import DynamicArray from "./dynamicArray";
import { post } from "../../core/request";
import { MinusSquareOutlined, PlusOutlined } from "@ant-design/icons";

const { Option } = Select;

const { TextArea } = Input;
const { Content } = Layout;

const layout = {
  labelCol: { span: 0 },
  wrapperCol: { span: 0 },
};
const typeOption = ["ByteArray", "Address", "Hash160"];

@withTranslation()
@inject("walletStore")
@withRouter
@observer
class Contractinvoke extends React.Component {
  constructor(props) {
    super(props);
    this.state = {
      size: 'default',
        path:"",
        disabled:false,
        visible: false,
        modal:false,
        loading:false,
        methods:[],
        params:[],
        methodselect:""
      };
    }
    toHome = () =>{
      location.href=location.origin;
    }
    showDrawer = () => {
      this.setState({
        visible: true,
      });
    };
    onClose = () => {
      this.setState({
        visible: false,
      });
    };
    showDetail = () =>{
      this.searchContract(res=>{
        this.refs.formRef.resetFields()
        this.setState({
          hash:res.contractHash,
          methods:res.manifest.abi.methods,
          params:[],
          tresult:""
        })
        this.refs.formRef.setFieldsValue({
          guihash:this.state.hash
        })
      });
      console.log(this.refs.formRef);
  };
  searchContract = (callback) => {
    const { t } = this.props;
    let _hash = this.refs.sinput.input.value.trim();
    if (!_hash) {
      message.info(t("contract.search input check"));
      return;
    }
    this.setState({ loading: true });
    var _this = this;
    var params = { contractHash: _hash };
    post("GetContract", params)
      .then(function (response) {
        var _data = response.data;
        _this.setState({loading:false});
        if(_data.msgType === -1){
          _this.setState({
            methods: [],
            params: [],
            tresult: "",
          });
          _this.refs.formRef.resetFields();
          _this.refs.formRef.setFieldsValue({
            guihash: _hash,
          });
          message.info(t("contract.search fail"));
          return;
        }else if(_data.msgType === 3){
          callback(_data.result)
        }
      })
      .catch(function (error) {
        console.log(error);
      });
  };
  showPara = (e) => {
    this.setState({
      params: this.state.methods[e].parameters,
      methodselect: this.state.methods[e],
    });
  };
  makeParams = (data) => {
    let method = this.state.methodselect;

    let _params = {
      contractHash: this.state.hash,
      method: method.name,
      parameters: [],
      cosigners: [],
      sendTx: false,
    };

    let inside = new Array();
    this.state.params.map((item, index) => {
      //深拷贝
      let _item = JSON.parse(JSON.stringify(item));

      let _type = method.parameters[index].type.toLowerCase();
      _item.value = data[_item.name];

        //检测字符串中是否包含两种类型值
        if(_type.search(/(hash160)|(bytearray)/g) !== -1 && typeof _item.value  === "object"){
          _item.type = _item.value.type;
          _item.value = _item.value.string;
        }

        if(_type === 'any'){
          _item.type = 'String';
          _item.value = _item.value;
        }
        
        if(_type === 'array'){
          _item.value = JSON.parse(_item.value);
        }
        inside = inside.concat(_item);
      })
      if(inside) _params.parameters = inside;

    //构造consigners
    let cosigners = new Array();
    data.cosigners
      ? data.cosigners.map((item) => {
          let _list = {};
          _list.account = item;
          cosigners = cosigners.concat(_list);
        })
      : null;
    if (cosigners) _params.cosigners = cosigners;

    return _params;
  };
  onFill = () => {
    this.refs.formRef.setFieldsValue({
      tresult: this.state.tresult,
    });
  };
  testInvoke = () => {
    const { t } = this.props;
    this.setState(
      {
        tresult: "",
      },
      this.onFill()
    );
    this.refs.formRef
      .validateFields()
      .then((data) => {
        let params = this.makeParams(data);

        this.invokeContract(params, (res) => {
          this.setState(
            {
              tresult: JSON.stringify(res.result),
            },
            this.onFill()
          );
        });
      })
      .catch(function (error) {
        message.error(t("input.correct"));
      });
  };
  invoke = (fieldsValue) => {
    let params = this.makeParams(fieldsValue);
    params.sendTx = true;
    const { t } = this.props;
    var _this = this;

    this.invokeContract(params, (res) => {
      _this.setState(
        {
          tresult: "",
        },
        _this.onFill()
      );
      _this.refs.formRef.resetFields();

      Modal.success({
        title: t("contract.invoke contract"),
        width: 650,
        content: (
          <div className="show-pri">
            <p>TxID : {res.result.txId ? res.result.txId : "--"}</p>
            <p>
              GAS : {res.result.gasConsumed ? res.result.gasConsumed : "--"}
            </p>
          </div>
        ),
        okText: t("button.ok"),
      });
    });
  };
  invokeContract = (params, callback) => {
    const { t } = this.props;
    post("InvokeContract", params)
      .then(function (res) {
        var _data = res.data;
        if (_data.msgType === -1) {
          Modal.error({
            title: t("contract.fail title"),
            width: 600,
            content: (
              <div className="show-pri">
                <p>
                  {t("error code")}: {_data.error.code}
                </p>
                <p>
                  {t("error msg")}: {_data.error.message}
                </p>
              </div>
            ),
            okText: t("button.ok"),
          });
          return;
        } else if (_data.msgType === 3) {
          callback(_data);
        }
      })
      .catch(function (error) {
        console.log(error);
      });
    }
    handleCancel = () => {
      this.setState({
        modal: false,
      });
    };
    makeArray = () =>{
      this.setState({
        modal: true
      });
    }
    handleparam = (val) =>{
      if(val.length <= 0) return "";
      this.handleCancel()
      return JSON.stringify(val);
    }
    onOk = () => {
      form.submit();
    };
    render = () =>{
    const {methods,params,disabled} = this.state;
    const accounts = this.props.walletStore.accountlist;
    const { t } = this.props;
    return (
      <Layout className="gui-container">
        <Sync />

        <Content className="mt3">
          <Row
            gutter={[30, 0]}
            className="bg-white pv4"
            style={{ minHeight: "calc( 100vh - 150px )" }}
          >
            <Col span={24}>
              <a className="fix-btn" onClick={this.showDrawer}>
                <SwapOutlined />
              </a>
              <PageHeader title={t("contract.invoke contract")}></PageHeader>

              {/* <DynamicArray handleparam={this.handleparam.bind(this)}/> */}
              <Form ref="formRef" className="trans-form" onFinish={this.invoke}>
                <Row className="mt3 mb5">
                  <Col span={20}>
                    <Form.Item
                      name="guihash"
                      label={t("contract.scripthash")}
                      rules={[
                        {
                          required: true,
                          message: t("contract.search fail"),
                        },
                      ]}
                    >
                      {/* <Input defaultValue="0x9bde8f209c88dd0e7ca3bf0af0f476cdd8207789" ref="sinput" placeholder="Scripthash"/> */}
                      <Input ref="sinput" placeholder="Scripthash" />
                    </Form.Item>

                    <Form.Item
                      name="guimethod"
                      label={t("contract.invoke method")}
                      rules={[
                        {
                          required: true,
                          message: t("input.required"),
                        },
                      ]}
                    >
                      <Select
                        placeholder={t("contract.select method")}
                        style={{ width: "100%" }}
                        onChange={this.showPara}
                      >
                        {methods.map((item, index) => {
                          return <Option key={index}>{item.name}</Option>;
                        })}
                      </Select>
                    </Form.Item>
                    {params[0] ? (
                      <div className="param-title">
                        <span>*</span> {t("contract.parameters")} :
                      </div>
                    ) : null}
                    {params.map((item, index) => {
                      return (
                        <div key={index}>
                          {item.type.toLowerCase() === "bytearray" ||
                          item.type.toLowerCase() == "hash160" ? (
                            <Form.Item
                              className="spec-row"
                              label={<span>{item.name}</span>}
                            >
                              <Input.Group compact>
                                <Form.Item
                                  name={[item.name, "type"]}
                                  style={{
                                    display: "inline-block",
                                    width: "30%",
                                  }}
                                  rules={[{ required: true, message: "type" }]}
                                >
                                  <Select
                                    placeholder="Select Type"
                                    style={{ width: "100%" }}
                                  >
                                    <Option value="ByteArray">ByteArray</Option>
                                    <Option value="Hash160">Hash160</Option>
                                    <Option value="Address">Address</Option>
                                  </Select>
                                </Form.Item>
                                <Form.Item
                                  name={[item.name, "string"]}
                                  style={{
                                    display: "inline-block",
                                    width: "70%",
                                  }}
                                  rules={[
                                    { required: true, message: "String" },
                                  ]}
                                >
                                  <Input
                                    style={{ width: "100%" }}
                                    placeholder="String"
                                  />
                                </Form.Item>
                              </Input.Group>
                            </Form.Item>
                          ) : (
                            <Form.Item
                              className="param-input"
                              name={item.name}
                              // getValueFromEvent={this.handleparam}
                              label={<span>{item.name}</span>}
                              rules={[
                                {
                                  required: true,
                                  message: t("input.required"),
                                },
                              ]}
                            >
                              {/* {item.type.toLowerCase() == 'array' ?
                        <Input.Search
                          defaultValue="11111"
                          placeholder="请点击Button构造array - 未翻译"
                          enterButton="构造Array"
                          // getValueFromEvent={this.handleparam}
                          onSearch={this.makeArray}
                        />:<Input placeholder={item.type}/>} */}
                        {item.type.toLowerCase() === 'any'?
                        <Input placeholder="String"/>:
                        <Input placeholder={item.type}/>}
                      </Form.Item>)
                      }
                    </div>
                  )}
                )}
                <Form.Item
                  name="cosigners"
                  label={t("contract.cosigners")}>
                  <Select
                    placeholder={t("contract.choose account")}
                    mode="tags"
                    style={{ width: '100%'}}>
                    {accounts.map((item)=>{
                      return(
                      <Option key={item.address}>{item.address}</Option>
                      )
                    })}
                  </Select>
                </Form.Item>
              </Col>
              <Col span={4}>
                <Button className="w200 form-btn" onClick={this.showDetail} loading={this.state.loading}>{t("button.search")}</Button>
              </Col>
            </Row>
            <Form.Item className="text-c w200" >
              <Button type="primary"  htmlType="button" onClick={this.testInvoke}>
               {t('button.test invoke')}
              </Button>
            </Form.Item>
            <div className="pa3 mb5">
              <p className="mb5 bolder">{t('contract.test result')}</p>
              <TextArea rows={3} value={this.state.tresult}/>
            </div>
            <Form.Item className="text-c w200">
              <Button className="mt3" type="primary" htmlType="submit" disabled={disabled} loading={this.state.iconLoading}>
                {t("button.send")}
              </Button>
            </Form.Item>
          </Form>
          </Col>
        </Row>
        <Modal
          title="构造Array-未翻译"
          visible={this.state.modal}
          footer={null}
          onCancel={this.handleCancel}
          width={600}
        >
          <DynamicArray handleparam={this.handleparam.bind(this)}/>
        </Modal>
        <Datatrans visible={this.state.visible} onClose={this.onClose} />
      </Content>
    </Layout>
    );
  };
}

export default Contractinvoke;
