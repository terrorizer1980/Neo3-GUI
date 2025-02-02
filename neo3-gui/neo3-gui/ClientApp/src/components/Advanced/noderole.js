/* eslint-disable */
import React from 'react';
import 'antd/dist/antd.css';
import axios from 'axios';
import {
  Checkbox,
  PageHeader,
  Modal,
  Alert,
  Row,
  Col,
  Form,
  Select,
  Button, 
  message} from 'antd';
import { Layout } from 'antd';
import Sync from '../sync';
import { observer, inject } from "mobx-react";
import { withRouter } from "react-router-dom";
import { withTranslation } from "react-i18next";
import "../../static/css/advanced.css";

const { Option } = Select;
const { Content } = Layout;

@withTranslation()
@observer
@withRouter
class Advancednoderole extends React.Component {
  constructor(props) {
    super(props);
    this.state = {
      size: 'default',
      rolelist: [{type:4,role:"StateValidator"},{type:8,role:"Oracle"}],
    };
  }
  onRole = fieldsValue =>{
    const { t } = this.props;
      console.log(fieldsValue);

    axios.post('http://localhost:8081', {
      "id": "1",
      "method": "GetNodesByRole",
      "params":{
        "role":parseInt(fieldsValue.role)
    }
    })
    .then(function (response) {
      var _data = response.data;
      console.log(_data);
      return;
    })
    .catch(function (error) {
      console.log(error);
      console.log("error");
    });
  }
  render = () => {
    const { t } = this.props;
    const { disabled, rolelist } = this.state;
    return (
      <Layout className="gui-container">
        <Sync />
        <Content className="mt3">
          <Row gutter={[30, 0]} style={{ 'minHeight': 'calc( 100vh - 120px )' }}>
            <Col span={24} className="bg-white pv4">
            <PageHeader title={t('根据角色查询节点')}></PageHeader>
            
            <div className="pa3">
            <Alert
                className="mt3 mb3"
                type="warning"
                message="XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX"
                showIcon
            />
            <Form ref="formRef" onFinish={this.onRole}>
                <h4 className="bolder mb4">{t('节点角色类型')}</h4>
                <Form.Item
                name="role"
                className="select-role"
                rules={[
                    {
                    required: true,
                    message: t("选择想要查询的节点"),
                    },
                ]}
                >
                <Select
                placeholder={t("advanced.select address")}
                style={{ width: '100%' }}>
                  {rolelist.map((item)=>{
                    return(
                    <Option key={item.type}>{item.role}</Option>
                    )
                  })}
                </Select>
                </Form.Item>
                <p className="text-c mt3">
                  <Button type="primary" htmlType="submit" disabled={disabled} loading={this.state.iconLoading}>
                    {t("button.confirm")}
                  </Button>
                </p>
              </Form>
              </div>
            </Col>
          </Row>
        </Content>
      </Layout>
    );
  }
}

export default Advancednoderole;

