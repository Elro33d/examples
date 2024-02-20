require('dotenv').config();
const TelegramBot = require('node-telegram-bot-api');
const https = require('https');

const token = process.env.TELEGRAM_BOT_TOKEN;
const bot = new TelegramBot(token, {polling: true});

const links = new Map();
const linksSent = new Map();
const accessList = new Set();
const adminList = new Set(['elroy_katz', 'sashelim']);

adminList.forEach(admin => accessList.add(admin));

function checkLinkAndSendMessage(chatId, link) {
    https.get(link, (res) => {
        if (res.statusCode !== 200) {
            bot.sendMessage(chatId, `שגיאה: ${res.statusCode}`);
        } else {
            if (!links.has(chatId)) {
                links.set(chatId, new Set());
            }
            links.get(chatId).add(link);
            bot.sendMessage(chatId, `קישור נוסף: ${link}`);
        }
    }).on('error', (e) => {
        console.error(e);
        bot.sendMessage(chatId, `אין אפשרות לגשת לקישור: ${link}.`);
    });
}

bot.onText(/\/add\s+(.+)/, (msg, match) => {
    const chatId = msg.chat.id;
    const link = match[1];
    checkLinkAndSendMessage(chatId, link);
});

bot.onText(/\/show/, (msg) => {
    const chatId = msg.chat.id;
    if (!accessList.has(msg.from.username)) {
        bot.sendMessage(chatId, 'אין לך גישה לפקודה זו.');
        return;
    }
    const chatLinks = links.get(chatId);
    if (!chatLinks || chatLinks.size === 0) {
        bot.sendMessage(chatId, 'לא נוספו קישורים עדיין.');
    } else {
        const linksMessage = Array.from(chatLinks).join('\n');
        bot.sendMessage(chatId, `קישורים שנמצאים במעקב: \n${linksMessage}`);
    }
});

bot.onText(/\/delete\s+(.+)/, (msg, match) => {
    const chatId = msg.chat.id;
    const linkToDelete = match[1];
    if (!accessList.has(msg.from.username)) {
        bot.sendMessage(chatId, 'אין לך גישה לפקודה זו.');
        return;
    }
    const chatLinks = links.get(chatId);
    if (chatLinks && chatLinks.delete(linkToDelete)) {
        bot.sendMessage(chatId, `הקישור ${linkToDelete} נמחק.`);
        if (chatLinks.size === 0) {
            links.delete(chatId);
        }
    } else {
        bot.sendMessage(chatId, `הקישור ${linkToDelete} לא נמצא.`);
    }
});

bot.onText(/\/help/, (msg) => {
    const chatId = msg.chat.id;
    let commandsList = [
        '/add - הוסף קישור למעקב',
        '/show - הצג את רשימת הקישורים שנמצאים במעקב',
        '/delete - מחק קישור מרשימת הקישורים שנמצאים במעקב'
    ];
    if (adminList.has(msg.from.username)) {
        commandsList = commandsList.concat([
            '/access_add - הוסף גישה למשתמש חדש',
            '/remove_access - הסר גישה של משתמש',
            '/admin_add - הפוך משתמש למנהל',
            '/remove_admin - מחק את יכולת הניהול של משתמש',
            '/shutdown - עצור את פעולת הבוט',
            '/admins - הצג רשימה של כל המנהלים של הבוט',
            '/users - הצג רשימה של כל המשתמשים בעלי גישה לבוט'
        ]);
    }
    bot.sendMessage(chatId, `רשימת פקודות: \n${commandsList.join('\n')}`);
});

bot.onText(/\/access_add\s+(.+)/, (msg, match) => {
    const chatId = msg.chat.id;
    const newUser = match[1];
    if (adminList.has(msg.from.username)) {
        accessList.add(newUser);
        bot.sendMessage(chatId, `גישה ניתנה ל ${newUser}.`);
    } else {
        bot.sendMessage(chatId, 'אין לך גישה לפקודה זו.');
    }
});

bot.onText(/\/remove_access\s+(.+)/, (msg, match) => {
    const chatId = msg.chat.id;
    const userToRemove = match[1];
    if (adminList.has(msg.from.username) && accessList.delete(userToRemove)) {
        bot.sendMessage(chatId, `הגישה של ${userToRemove} הוסרה.`);
    } else {
        bot.sendMessage(chatId, `${userToRemove} אינו נמצא ברשימת הגישה לבוט או שאין לך גישה.`);
    }
});

bot.onText(/\/admin_add\s+(.+)/, (msg, match) => {
    const chatId = msg.chat.id;
    const newAdmin = match[1];
    if (adminList.has(msg.from.username)) {
        adminList.add(newAdmin);
        accessList.add(newAdmin); // Ensure new admin also has access
        bot.sendMessage(chatId, `${newAdmin} קיבל גישה לניהול הבוט.`);
    } else {
        bot.sendMessage(chatId, 'אין לך גישה לפקודה זו.');
    }
});

bot.onText(/\/remove_admin\s+(.+)/, (msg, match) => {
    const chatId = msg.chat.id;
    const adminToRemove = match[1];
    if (adminList.has(msg.from.username) && adminList.delete(adminToRemove)) {
        bot.sendMessage(chatId, `יכולת הניהול של ${adminToRemove} הוסרה.`);
    } else {
        bot.sendMessage(chatId, `${adminToRemove} אינו מנהל או שאין לך גישה.`);
    }
});

bot.onText(/\/shutdown/, (msg) => {
    const chatId = msg.chat.id;
    if (adminList.has(msg.from.username)) {
        bot.sendMessage(chatId, 'עוצר את הבוט...');
        bot.stopPolling();
    } else {
        bot.sendMessage(chatId, 'אין לך גישה לפקודה זו.');
    }
});

bot.onText(/\/admins/, (msg) => {
    const chatId = msg.chat.id;
    if (adminList.has(msg.from.username)) {
        const adminUsernames = Array.from(adminList).join(', ');
        bot.sendMessage(chatId, `מנהלים: ${adminUsernames}`);
    } else {
        bot.sendMessage(chatId, 'אין לך גישה לפקודה זו.');
    }
});

bot.onText(/\/users/, (msg) => {
    const chatId = msg.chat.id;
    if (adminList.has(msg.from.username)) {
        const accessUsernames = Array.from(accessList).join('\n');
        bot.sendMessage(chatId, `משתמשים בעלי גישה לבוט: \n${accessUsernames}`);
    } else {
        bot.sendMessage(chatId, 'אין לך גישה לפקודה זו.');
    }
});

bot.on('message', (msg) => {
  if (msg.text.startsWith('/') && !commands.includes(msg.text.split(' ')[0])) {
    bot.sendMessage(msg.chat.id, 'פקודה לא ידועה, בבקשה נסה שוב.');
  }
});

setInterval(() => {
  links.forEach((userLinks, chatId) => {
    userLinks.forEach(link => {
      https.get(link, (res) => {
        res.on('data', (chunk) => {
          if (chunk.toString().includes('To join the CollaNote:') && !linksSent.get(link)) {
            bot.sendMessage(chatId, `נראה שכרגע יש מקום בגרסת הבטא של ${link}`);
            linksSent.set(link, true);
          } else if (chunk.toString().includes('This beta is full') && linksSent.get(link)) {
            bot.sendMessage(chatId, `המקום בקישור ${link} אינו קיים יותר`);
            linksSent.set(link, false);
          }
        });
        res.on('error', (err) => {
          console.error(err);
        });
      });
    });
  });
}, 3000);
