/* eslint-disable */
import React from 'react';
import _ from 'lodash';
import '../../static/css/trans.css';
import { Link } from 'react-router-dom';
import { Layout, Row, Col, Tabs, message, PageHeader, Divider } from 'antd';
import { Hashdetail, Attrlist, Translist, Witlist,Scriptlist} from './translog';
import Notifies from './translog';
import Datatrans from '../Common/datatrans';
import { withRouter } from "react-router-dom";
import Sync from '../sync';
import { useTranslation, withTranslation } from "react-i18next";
import { post } from "../../core/request";

import { SwapOutlined,ArrowLeftOutlined } from '@ant-design/icons';

const { Content } = Layout;
const { TabPane } = Tabs;

@withTranslation()
@withRouter
class Transcon extends React.Component {
  constructor(props) {
    super(props);
    this.state = {
      visible: false,
      hashdetail: [],
      transfers: [],
      witnesses: [],
      attributes: [],
      notifies: [],
      script:"",
      scriptcode:[]
    };
  }
  componentDidMount() {
    this.getTransdetail(res => {
      this.setState({
        hashdetail: res,
        transfers: res.transfers,
        witnesses: res.witnesses,
        attributes: res.attributes,
        notifies: res.notifies,
        script:res.script,
        scriptcode:res.scriptCode,
      });
    });
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
  getTransdetail = callback => {
    var _this = this;
    let params = {
      "txId": location.pathname.split(":").pop()
    };
    post("GetTransaction",params).then(res =>{
      var _data = res.data;
      if (_data.msgType === -1) {
        message.error("查询失败");
        return;
      } else {
        callback(_data.result);
      }
    }).catch(function () {
      console.log("error");
      _this.props.history.goBack();
    });
  }
  notifiesData = () =>{
    console.log(this.state.notifies)
  }
  back=()=>{
    const { location, history } = this.props;
    const from = _.get(location, 'state.from', null);
    if (from) {
      history.replace(from);
    } else {
      history.goBack();
    }
  }
  render = () => {
    const { t } = this.props;
    const { hashdetail, transfers, witnesses, attributes,script,scriptcode } = this.state;
    return (
      <Layout className="gui-container">
        <Sync />
        <Content className="mt3">
          <Row gutter={[30, 0]} className="mb1">
            <Col span={24} className="bg-white pv4">
              <a className="fix-btn" onClick={this.showDrawer}><SwapOutlined /></a>
              <Tabs className="tran-title" defaultActiveKey="1" tabBarExtraContent={<ArrowLeftOutlined className="h2" onClick={this.back} />}>
                <TabPane tab={t("blockchain.transaction.content")} key="1">
                  <Hashdetail hashdetail={hashdetail} />
                  <Translist transfers={transfers} />
                  <Attrlist attributes={attributes} />
                  <Witlist witnesses={witnesses} />
                  <Scriptlist script={script} scriptcode={scriptcode}/>
                </TabPane>
                <TabPane tab={t("blockchain.transaction.log")} key="2">
                  <Notifies notifies={this.state.notifies} />
                </TabPane>
              </Tabs>
            </Col>
          </Row>
          <Datatrans visible={this.state.visible} onClose={this.onClose} />
        </Content>
      </Layout>
    );
  }
}

export default Transcon;