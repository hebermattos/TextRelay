FROM rabbitmq:3.13-management

ARG DELAYED_MESSAGE_PLUGIN_VERSION=3.13.0
ADD --chmod=0644 https://github.com/rabbitmq/rabbitmq-delayed-message-exchange/releases/download/v${DELAYED_MESSAGE_PLUGIN_VERSION}/rabbitmq_delayed_message_exchange-${DELAYED_MESSAGE_PLUGIN_VERSION}.ez /opt/rabbitmq/plugins/rabbitmq_delayed_message_exchange.ez

RUN rabbitmq-plugins enable --offline rabbitmq_delayed_message_exchange
